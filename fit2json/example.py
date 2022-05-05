import fit2json
import json
import datetime
from collections import defaultdict

src_file = 'data/....fit'
counter = 0

#converted = fit2json.convert_fit_to_json(src_file)
converted = fit2json.convert_fit_to_json(src_file, filter={'session', 'record'})
print(converted)

converted_dict = json.loads(converted)
activity_dict = dict()
records_dict = defaultdict(list)
uuid = 'pseudo-uuid-for-testing'
converter = 'fitConverter'

# build activity_dict with values from converted_dict
for frame in converted_dict:
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

            print(activity_dict)

# build records_dict with values from converted_dict
for frame in converted_dict:
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

print(json.dumps(records_dict))

activity_dict['records'] = records_dict['records']
print(json.dumps(activity_dict))

with open('output/output.json', 'w') as file:
    file.write(json.dumps(activity_dict))