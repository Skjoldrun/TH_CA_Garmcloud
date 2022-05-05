import logging
from collections import OrderedDict
import datetime
import types
import sys
import traceback
import json
import os
import requests
import azure.functions as func
import fitdecode
from collections import defaultdict


def main(req: func.HttpRequest) -> func.HttpResponse:
    logging.info('HTTPFitConverter received a request.')

    # get HttpGarmDataUrl from settings
    garm_data_url = os.environ["HttpGarmDataUrl"]

    if req.method == 'GET':
        uuid = req.params['uuid']
        if uuid == '':
            return func.HttpResponse(f'[HttpFitConverter] Bad Request: no uuid was given.',
                                     status_code=400)
        if uuid == "ping":
            return func.HttpResponse(f'[HttpFitConverter] Ping: Function is up and running.',
                                     status_code=200)
    if req.method == 'POST':
        try:
            uuid = req.params['uuid']
            file = req.files['file']
            converter = 'FitConverter'

            jsonstr = convert_fit_to_json(file)
            logging.info('Json String successfully converted!')
            jsonstr = compute_json(jsonstr, uuid, converter)
            logging.info('Json String successfully computed!')
            post_json(jsonstr, uuid, converter, garm_data_url)
            return func.HttpResponse(f'[HttpFitConverter] FIT Json with uuid {uuid} sent to GarmData function.',
                                     status_code=200)

        except Exception as e:
            logging.error(e)
            return func.HttpResponse(f'Error while processing the Request: {e}', status_code=500)

    return func.HttpResponse(f'[HttpFitConverter] Bad Request.',
                             status_code=400)


def compute_json(jsonstr, uuid, converter):
    jsonstr_dict = json.loads(jsonstr)
    activity_dict = dict()
    records_dict = defaultdict(list)

    # build activity_dict with values from converted_dict
    for frame in jsonstr_dict:
        if frame['frame_type'] == 'data_message':
            if frame['name'] == 'session':
                activity_dict['uuid'] = uuid
                activity_dict['converter'] = converter
                for field in frame['fields']:
                    if field['name'] == 'total_elapsed_time':
                        activity_dict['total_time_in_sec'] = field['value']
                    if field['name'] == 'total_distance':
                        activity_dict['total_dist_in_km'] = field['value']
                    if field['name'] == 'enhanced_avg_speed':
                        activity_dict['avg_speed_in_kmh'] = field['value']
                    if field['name'] == 'avg_heart_rate':
                        activity_dict['avg_heart_rate'] = field['value']

    # build records_dict with values from converted_dict
    for frame in jsonstr_dict:
        if frame['frame_type'] == 'data_message':
            if frame['name'] == 'record':
                record_dict = dict()
                record_dict['activity_uuid'] = uuid
                for field in frame['fields']:
                    if field['name'] == 'timestamp':
                        # reformat timestamp
                        date_time_obj = datetime.datetime.strptime(field['value'], '%Y-%m-%dT%H:%M:%S+00:00')
                        record_dict['timestamp'] = date_time_obj.strftime('%Y-%m-%d %H:%M:%S')
                    if field['name'] == 'position_lat':
                        record_dict['lat'] = field['value']
                    if field['name'] == 'position_long':
                        record_dict['lon'] = field['value']
                    if field['name'] == 'distance':
                        record_dict['distance'] = field['value']
                    if field['name'] == 'enhanced_altitude':
                        record_dict['ele'] = field['value']
                    if field['name'] == 'enhanced_speed':
                        record_dict['speed'] = field['value']
                    if field['name'] == 'heart_rate':
                        record_dict['heart_rate'] = field['value']

                # append record to records list ind dict
                records_dict['records'].append(record_dict)

    activity_dict['records'] = records_dict['records']
    return json.dumps(activity_dict)


def post_json(jsonstr, uuid, converter, url):
    response = requests.post(url, params={'uuid': uuid, 'converter': converter}, data=jsonstr)


# infile    - file to be converted
# nocrc     - fitdecode.CrcCheck.DISABLED ignores all, READONLY just reads and ENABLED raises error if it doesn't match
# nodef     - Do not output FIT so-called local message definitions if set to False
# filter    - Message name(s) or global numbers to filter (other messages are then ignored), e.g. {"record", "session"}
def convert_fit_to_json(infile, nocrc=fitdecode.CrcCheck.READONLY, nodef=True, filter=None):
    frames = []
    exception_msg = None
    try:
        with fitdecode.FitReader(
                infile,
                processor=fitdecode.StandardUnitsDataProcessor(),
                check_crc=nocrc,
                keep_raw_chunks=True) as fit:
            for frame in fit:
                if nodef and isinstance(
                        frame, fitdecode.FitDefinitionMessage):
                    continue

                if (filter and
                        isinstance(frame, (
                                fitdecode.FitDefinitionMessage,
                                fitdecode.FitDataMessage)) and
                        (frame.name not in filter) and
                        (frame.global_mesg_num not in filter)):
                    continue

                frames.append(frame)
    except Exception:
        print('WARNING: the following error occurred while parsing FIT file.', file=sys.stderr)
        print('', file=sys.stderr)
        traceback.print_exc()

    converted = json.dumps(frames, cls=RecordJSONEncoder)
    return converted


class RecordJSONEncoder(json.JSONEncoder):
    def default(self, obj):
        if isinstance(obj, types.GeneratorType):
            return list(obj)

        if isinstance(obj, datetime.time):
            return obj.isoformat()

        if isinstance(obj, datetime.datetime):
            return obj.isoformat()

        if isinstance(obj, fitdecode.FitChunk):
            return OrderedDict((
                ('index', obj.index),
                ('offset', obj.offset),
                ('size', len(obj.bytes))))

        if isinstance(obj, fitdecode.types.FieldDefinition):
            return OrderedDict((
                ('name', obj.name),
                ('def_num', obj.def_num),
                ('type_name', obj.type.name),
                ('base_type_name', obj.base_type.name),
                ('size', obj.size)))

        if isinstance(obj, fitdecode.types.DevFieldDefinition):
            return OrderedDict((
                ('name', obj.name),
                ('dev_data_index', obj.dev_data_index),
                ('def_num', obj.def_num),
                ('type_name', obj.type.name),
                ('size', obj.size)))

        if isinstance(obj, fitdecode.types.FieldData):
            return OrderedDict((
                ('name', obj.name),
                ('value', obj.value),
                ('units', obj.units if obj.units else ''),
                ('def_num', obj.def_num),
                ('raw_value', obj.raw_value)))

        if isinstance(obj, fitdecode.FitHeader):
            crc = obj.crc if obj.crc else 0
            return OrderedDict((
                ('frame_type', 'header'),
                ('header_size', obj.header_size),
                ('proto_ver', obj.proto_ver),
                ('profile_ver', obj.profile_ver),
                ('body_size', obj.body_size),
                ('crc', f'{crc:#06x}'),
                ('crc_matched', obj.crc_matched),
                ('chunk', obj.chunk)))

        if isinstance(obj, fitdecode.FitCRC):
            return OrderedDict((
                ('frame_type', 'crc'),
                ('crc', f'{obj.crc:#06x}'),
                ('matched', obj.matched),
                ('chunk', obj.chunk)))

        if isinstance(obj, fitdecode.FitDefinitionMessage):
            return OrderedDict((
                ('frame_type', 'definition_message'),
                ('name', obj.name),
                ('header', OrderedDict((
                    ('local_mesg_num', obj.local_mesg_num),
                    ('time_offset', obj.time_offset),
                    ('is_developer_data', obj.is_developer_data)))),
                ('global_mesg_num', obj.global_mesg_num),
                ('endian', obj.endian),
                ('field_defs', obj.field_defs),
                ('dev_field_defs', obj.dev_field_defs),
                ('chunk', obj.chunk)))

        if isinstance(obj, fitdecode.FitDataMessage):
            return OrderedDict((
                ('frame_type', 'data_message'),
                ('name', obj.name),
                ('header', OrderedDict((
                    ('local_mesg_num', obj.local_mesg_num),
                    ('time_offset', obj.time_offset),
                    ('is_developer_data', obj.is_developer_data)))),
                ('fields', obj.fields),
                ('chunk', obj.chunk)))

        # fall back to original to raise a TypeError
        return super().default(obj)
