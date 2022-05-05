from collections import OrderedDict
import datetime
import json
import types
import sys
import traceback

import fitdecode


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
