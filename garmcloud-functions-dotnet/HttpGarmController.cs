using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace GarmCloud.Functions
{
    public static class HttpGarmController
    {
        private static string fitConverterUrl = Environment.GetEnvironmentVariable("HttpFitConverterUrl");
        private static string gpxConverterUrl = Environment.GetEnvironmentVariable("HttpGpxConverterUrl");

        /// <summary>
        /// /// Controller accepts GET and POST HTTP requests.
        /// GET:
        /// Listens for UUID query param with GET and checks Blob storage for file with uuid as name. 
        /// Returns json from that file.
        /// POST:
        /// Listens for file form body to be processed and posted to a converter function. 
        /// Returns a generated UUID for getting the json data with GET requests later.
        /// </summary>
        /// <param name="req">request with data and query params</param>
        /// <param name="blobContainer">Blob container to be read</param>
        /// <param name="log">logger</param>
        /// <returns></returns>
        [FunctionName("HttpGarmController")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [Blob("garmdatacontainer")] CloudBlobContainer blobContainer,
            ILogger log)
        {
            log.LogInformation("------------------------------------------------------------------------");
            log.LogInformation("[HttpGarmController] HttpGarmController received a request.");

            if (req.Method == "GET")
            {
                log.LogInformation("[HttpGarmController] Request Type was GET.");
                string uuid = req.Query["UUID"];
                if (String.IsNullOrEmpty(uuid))
                {
                    log.LogInformation("[HttpGarmController] No given UUID. Aborting ...");
                    return new BadRequestResult();
                }

                // ping function
                if (uuid == "ping")
                {
                    log.LogInformation("[HttpGarmController] Functon was pinged and gets result: OK.");
                    return new OkObjectResult("HttpGarmController Ping: Function is up and running.");
                }

                // check Blob storage for given uuid
                return await readJsonFromBlob(blobContainer, log, uuid);
            }
            else if (req.Method == "POST")
            {
                log.LogInformation("[HttpGarmController] Request Type was POST.");
                var file = req.Form.Files["file"];
                if (file.Length > 0)
                {
                    return await processPostedFile(log, file);
                }
                log.LogInformation("[HttpGarmController] No file was uploaded.");
                return new BadRequestResult();
            }
            else
            {
                log.LogInformation($"[HttpGarmController] Request Type was {req.Method}. This doesn't get processed.");
                return new BadRequestResult();
            }
        }

        /// <summary>
        /// Processes the posted file.
        /// Generates the UUID as GUID for identifying the data from file.
        /// Checks extension for calling the propper converter.
        /// Calls the following converter Azure function.
        /// </summary>
        /// <param name="log">logger</param>
        /// <param name="file">file to be posted to converter function</param>
        /// <returns></returns>
        private static async Task<IActionResult> processPostedFile(ILogger log, IFormFile file)
        {
            // full path to file in temp location
            var filePath = Path.Combine(Path.GetTempPath(), file.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            log.LogInformation($"[HttpGarmController] File {file.FileName} with extension {Path.GetExtension(filePath)} received and saved temporarily stored in path {filePath}.");

            // Rename with GUID
            var uuid = Guid.NewGuid();
            var fileExtension = Path.GetExtension(filePath);
            var newFileName = $"{uuid}{fileExtension}";
            File.Move(filePath, Path.Combine(Path.GetTempPath(), newFileName));
            filePath = Path.Combine(Path.GetTempPath(), newFileName);
            log.LogInformation($"[HttpGarmController] File {file.FileName} renamed to {newFileName}");

            // prepare for calling converter functions with HttpClient and fileContent
            var httpClient = new HttpClient();
            var form = new MultipartFormDataContent();
            var fs = File.OpenRead(filePath);
            var streamContent = new StreamContent(fs);
            var fileContent = new ByteArrayContent(await streamContent.ReadAsByteArrayAsync());
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");

            // "file" parameter name must be the same as in the converter function
            form.Add(fileContent, "file", Path.GetFileName(filePath));

            var queryString = new Dictionary<string, string>() { { "uuid", uuid.ToString() } };
            HttpResponseMessage response;
            if (fileExtension.ToLower() == ".fit")
            {
                // add uuid as query param to uri, then add json as string content and POST
                var requestUri = QueryHelpers.AddQueryString(fitConverterUrl, queryString);
                response = await httpClient.PostAsync(requestUri, form);
                log.LogInformation($"[HttpGarmController] Function HttpFitConverter response status code: {response.StatusCode}");
            }
            else if (fileExtension.ToLower() == ".gpx")
            {
                // add uuid as query param to uri, then add json as string content and POST
                var requestUri = QueryHelpers.AddQueryString(gpxConverterUrl, queryString);
                response = await httpClient.PostAsync(requestUri, form);
                log.LogInformation($"[HttpGarmController] Function HttpGpxConverter response status code: {response.StatusCode}");
            }
            else
            {
                log.LogInformation("[HttpGarmController] No file was uploaded.");
                return new BadRequestResult();
            }
            return new OkObjectResult(uuid);
        }

        /// <summary>
        /// Reads the json result from Blob storage.
        /// Returns 404 not found, if the result is not there yet.
        /// Returns data as json string, if file was found.
        /// </summary>
        /// <param name="blobContainer">Blob container to be read</param>
        /// <param name="log">logger</param>
        /// <param name="uuid">uuid for identifying the data</param>
        /// <returns></returns>
        private static async Task<IActionResult> readJsonFromBlob(CloudBlobContainer blobContainer, ILogger log, string uuid)
        {
            try
            {
                await blobContainer.CreateIfNotExistsAsync();
                var blobName = $"{uuid}.json";
                var cloudBlockBlob = blobContainer.GetBlockBlobReference(blobName);

                using (var filestream = await cloudBlockBlob.OpenReadAsync())
                {
                    var streamreader = new StreamReader(filestream);
                    var jsonFromFile = await streamreader.ReadToEndAsync();
                    log.LogInformation($"[HttpGarmController] Finished reading from Blob file: {uuid}.json.");
                    return new OkObjectResult(jsonFromFile);
                }
            }
            catch (Exception e)
            {
                log.LogError($"[HttpGarmController] Error while reading from Blob: {e}");
                return new NotFoundObjectResult(uuid);
            }
        }
    }
}