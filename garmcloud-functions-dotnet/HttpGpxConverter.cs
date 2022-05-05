using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;


namespace GarmCloud.Functions
{
    public static class HttpGpxConverter
    {

        private static string garmDataUrl = Environment.GetEnvironmentVariable("HttpGarmDataUrl");
        private static string converter = "GpxConverter";

        /// <summary>
        /// HttpGpxConverter converts GPX data from a given file to json, based on a Activity object.
        /// </summary>
        /// <param name="req">request with data and query params</param>
        /// <param name="log">logger</param>
        /// <returns></returns>
        [FunctionName("HttpGpxConverter")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {

            log.LogInformation("------------------------------------------------------------------------");
            log.LogInformation("[HttpGpxConverter] HttpGpxConverter received a request.");

            if (req.Method == "GET")
            {
                log.LogInformation("[HttpGpxConverter] Request Type was GET.");
                string uuid = req.Query["uuid"];
                if (String.IsNullOrEmpty(uuid))
                {
                    log.LogInformation("[HttpGpxConverter] No given uuid. Aborting ...");
                    return new BadRequestResult();
                }

                // ping function
                if (uuid == "ping")
                {
                    log.LogInformation("[HttpGpxConverter] Functon was pinged and gets result: OK.");
                    return new OkObjectResult("HttpGpxConverter Ping: Function is up and running.");
                }
                return new BadRequestResult();
            }
            else if (req.Method == "POST")
            {
                log.LogInformation("[HttpGpxConverter] Request Type was POST.");

                try
                {
                    // read file from POST
                    var file = req.Form.Files["file"];
                    var uuid = req.Query["uuid"];

                    if (file.Length > 0)
                    {
                        // full path to file in temp location and random name
                        var filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        // convert gpx to json
                        XmlSerializer xmlSerializer = new XmlSerializer(typeof(XmlSerializeGpx.Gpx), "http://www.topografix.com/GPX/1/1");
                        XmlReader reader = XmlReader.Create(filePath);

                        XmlSerializeGpx.Gpx gpxObj = (XmlSerializeGpx.Gpx)xmlSerializer.Deserialize(reader);
                        // filter specific data from gpx object
                        var jsonStr = GpxToJson(gpxObj, uuid, converter);

                        // post jsonStr to GarmData
                        PostJson(jsonStr, uuid, garmDataUrl, log);

                        return new OkObjectResult($"[HttpGpxConverter] GPX Json with uuid {uuid} sent to GarmData function.");
                    }
                    log.LogInformation("[HttpGpxConverter] No file was uploaded.");
                    return new BadRequestResult();
                }
                catch (Exception e)
                {
                    log.LogError($"[HttpGpxConverter] Error: {e}");
                    return new BadRequestResult();
                }
            }
            else
            {
                log.LogInformation($"[HttpGpxConverter] Request Type was {req.Method}. This doesn't get processed.");
                return new BadRequestResult();
            }
        }

        /// <summary>
        /// Computes the given object and convertes it to an Activity object.
        /// </summary>
        /// <param name="gpxObj">object to be converted</param>
        /// <param name="uuid">uuid for identifying the data</param>
        /// <param name="converter">converter name string</param>
        /// <returns></returns>
        private static string GpxToJson(XmlSerializeGpx.Gpx gpxObj, string uuid, string converter)
        {
            var trackpoints = gpxObj.trk.trkseg.trkpt;
            var activity = new Activity()
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
                activity.records.Add(record);
            }
            return JsonConvert.SerializeObject(activity);
        }

        /// <summary>
        /// Posts the json string to the HttpGarmData Azure function.
        /// </summary>
        /// <param name="jsonStr">json string to be posted</param>
        /// <param name="uuid">uuid for identifying the data</param>
        /// <param name="url">url of the receiving function</param>
        /// <param name="log">logger</param>
        private static async void PostJson(string jsonStr, string uuid, string url, ILogger log)
        {
            var queryString = new Dictionary<string, string>() { { "uuid", uuid }, { "converter", "GpxConverter" } };
            using (var client = new HttpClient())
            {
                // add uuid as query param to uri, then add json as string content and POST
                var requestUri = QueryHelpers.AddQueryString(url, queryString);
                var response = await client.PostAsync(requestUri, new StringContent(jsonStr, Encoding.UTF8, "application/json"));
                log.LogInformation($"[HttpGpxConverter] Post Request for uuid {uuid} sent to url: {requestUri}");
            }
        }
    }

    /// <summary>
    /// Class for the generation of Activity objects
    /// </summary>
    public class Activity
    {
        public string uuid { get; set; }
        public string converter { get; set; }
        public double? total_time_in_sec { get; set; }
        public double? total_dist_in_km { get; set; }
        public double? avg_speed_in_kmh { get; set; }
        public int? avg_heart_rate { get; set; }
        public List<Record> records { get; set; }
    }

    /// <summary>
    /// Class for generation of Record objects
    /// </summary>
    public class Record
    {
        public string activity_uuid { get; set; }
        public string timestamp { get; set; }
        public double? lat { get; set; }
        public double? lon { get; set; }
        public double? distance { get; set; }
        public double? ele { get; set; }
        public double? speed { get; set; }
        public int? heart_rate { get; set; }
    }

    /// <summary>
    /// Class for serializing the GPX data.
    /// </summary>
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
