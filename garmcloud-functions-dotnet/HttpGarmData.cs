using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SqlKata.Compilers;
using SqlKata.Execution;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;
using System.Web.Http;

namespace GarmCloud.Functions
{
    public static class HttpGarmData
    {
        /// <summary>
        /// HttpGarmData stores a posted json string as file with given uuid in Blob.
        /// Stores the received data to a Azure SQL database.
        /// </summary>
        /// <param name="req">request with data and query params</param>
        /// <param name="outputContainer">Blob container to be written</param>
        /// <param name="log">logger</param>
        /// <returns></returns>
        [FunctionName("HttpGarmData")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [Blob("garmdatacontainer")] CloudBlobContainer outputContainer,
            ILogger log)
        {
            log.LogInformation("------------------------------------------------------------------------");
            log.LogInformation("[HttpGarmData] HttpGarmData received a request.");

            if (req.Method == "GET")
            {
                log.LogInformation("[HttpGarmData] Request Type was GET.");
                string uuid = req.Query["uuid"];
                if (String.IsNullOrEmpty(uuid))
                {
                    log.LogInformation("[HttpGarmData] No given uuid. Aborting ...");
                    return new BadRequestResult();
                }

                // ping function
                if (uuid == "ping")
                {
                    log.LogInformation("[HttpGarmData] Functon was pinged and gets result: OK.");
                    return new OkObjectResult("HttpGarmData Ping: Function is up and running.");
                }
                return new BadRequestResult();
            }
            else if (req.Method == "POST")
            {
                try
                {


                    var converter = req.Query["converter"];
                    var uuid = req.Query["uuid"];
                    log.LogInformation($"[HttpGarmData] Request was sent from {converter} with uuid {uuid}");

                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    var activity = JsonConvert.DeserializeObject<Activity>(requestBody);

                    // access Blob storage
                    await outputContainer.CreateIfNotExistsAsync();
                    var blobName = $"{uuid}.json";
                    var cloudBlockBlob = outputContainer.GetBlockBlobReference(blobName);
                    await cloudBlockBlob.UploadTextAsync(requestBody);
                    log.LogInformation("[HttpGarmData] Blob was written to storage.");

                    // access AzureDB
                    // example from https://docs.microsoft.com/en-us/azure/azure-functions/functions-scenario-database-table-cleanup
                    // sqlKata Lib https://sqlkata.com/
                    SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
                    builder.DataSource = "garmdbserver.database.windows.net";
                    builder.UserID = "garmadmin";
                    builder.Password = "642Mp455w02d";
                    builder.InitialCatalog = "garmdb";

                    using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                    {
                        var compiler = new SqlServerCompiler();
                        var db = new QueryFactory(connection, compiler);

                        var affected = db.Query("Activities").Insert(new
                        {
                            uuid = activity.uuid,
                            converter = activity.converter,
                            progress = "100%",
                            avg_speed_in_kmh = activity.avg_speed_in_kmh,
                            avg_heart_rate = activity.avg_heart_rate,
                            total_time_in_sec = activity.total_time_in_sec,
                            total_dist_in_km = activity.total_dist_in_km,
                        });
                        log.LogInformation($"[HttpGarmData] Activity was written to DB.");

                        foreach (var record in activity.records)
                        {
                            affected = db.Query("Records").Insert(new
                            {
                                activity_uuid = record.activity_uuid,
                                timestamp = record.timestamp,
                                lat = record.lat,
                                lon = record.lon,
                                distance = record.distance,
                                ele = record.ele,
                                speed = record.speed,
                                heart_rate = record.heart_rate
                            });
                        }
                        log.LogInformation($"[HttpGarmData] Records were written to DB.");
                    }
                    return new OkObjectResult(uuid);
                }
                catch (Exception e)
                {
                    log.LogError($"[HttpGpxConverter] Error: {e}");
                    return new BadRequestErrorMessageResult($"Error: {e}");
                }
            }
            return new BadRequestResult();
        }
    }
}
