using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace gpx2json
{
    class Program
    {
        private const string FILENAME = @"C:\Users\...\garmcloud\gpx2json\data\20200601-170957.gpx";
        //private const string FILENAME = @"C:\Users\...\garmcloud\gpx2json\data\Elzachrunde_Kandelblick.gpx";
        private const string converter = "GpxConverter";
        private const string uuid = "pseudo-uuid-for-testing";

        static void Main(string[] args)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(XmlSerializeGpx.Gpx), "http://www.topografix.com/GPX/1/1");

            FileStream fs = new FileStream(FILENAME, FileMode.Open);
            XmlReader reader = XmlReader.Create(fs);

            XmlSerializeGpx.Gpx gpxObj = (XmlSerializeGpx.Gpx)xmlSerializer.Deserialize(reader);
            var jsonStr = ComputeJson(gpxObj, uuid, converter);

            // write json to file
            TextWriter writer;
            using (writer = new StreamWriter(@"C:\Users\...\garmcloud\gpx2json\output\output.json", append: false))
            {
                writer.WriteLine(jsonStr);
            }

            // write to console
            Console.WriteLine($"GPX data: \n{jsonStr}");
        }

        private static string ComputeJson(XmlSerializeGpx.Gpx gpxObj, string uuid, string converter)
        {
            var trackpoints = gpxObj.trk.trkseg.trkpt;
            var gpxComputed = new GpxComputed()
            {
                uuid = uuid,
                converter = converter,
                records = new List<Record>()
            };

            foreach (var trackpoint in trackpoints)
            {
                var record = new Record()
                {
                    activity_uuid = uuid,
                    ele = trackpoint.ele,
                    timestamp = trackpoint.time.ToString("yyyy-MM-dd HH:mm:ss"),
                    lat = trackpoint.lat,
                    lon = trackpoint.lon
                };
                gpxComputed.records.Add(record);
            }
            return JsonConvert.SerializeObject(gpxComputed);
        }
    }

    public class GpxComputed
    {
        public string uuid { get; set; }
        public string converter { get; set; }
        public double total_time_in_sec { get; set; }
        public double total_dist_in_km { get; set; }
        public double avg_speed_in_kmh { get; set; }
        public int avg_heart_rate { get; set; }
        public List<Record> records { get; set; }
    }

    public class Record
    {
        public string activity_uuid { get; set; }
        public string timestamp { get; set; }
        public double lat { get; set; }
        public double lon { get; set; }
        public double distance { get; set; }
        public double ele { get; set; }
        public double speed { get; set; }
        public int heart_rate { get; set; }
    }

    public class XmlSerializeGpx
    {

        [XmlRoot(ElementName = "start", Namespace = "http://www.topografix.com/GPX/1/1")]
        public class Start
        {
            public double ele { get; set; }
            public DateTime time { get; set; }
            [XmlAttribute("lat")]
            public double lat { get; set; }
            [XmlAttribute("lon")]
            public double lon { get; set; }
        }

        [XmlRoot(ElementName = "trkpt", Namespace = "http://www.topografix.com/GPX/1/1")]
        public class Trkpt
        {
            public double ele { get; set; }
            public DateTime time { get; set; }
            [XmlAttribute("lat")]
            public double lat { get; set; }
            [XmlAttribute("lon")]
            public double lon { get; set; }
        }

        [XmlRoot(ElementName = "trkseg", Namespace = "http://www.topografix.com/GPX/1/1")]
        public class Trkseg
        {
            [XmlElement("start")]
            public List<Start> start { get; set; }
            [XmlElement("trkpt")]
            public List<Trkpt> trkpt { get; set; }
        }

        [XmlRoot(ElementName = "trk", Namespace = "http://www.topografix.com/GPX/1/1")]
        public class Trk
        {
            public Trkseg trkseg { get; set; }
        }

        [XmlRoot(ElementName = "gpx", Namespace = "http://www.topografix.com/GPX/1/1")]
        public class Gpx
        {
            public Trk trk { get; set; }
        }
    }
}
