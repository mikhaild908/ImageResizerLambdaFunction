using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;

using Amazon.S3;
using Amazon.S3.Util;
using Amazon.S3.Model;
using System.IO;
using Amazon.S3.Transfer;
using System.Threading;
using ImageMagick;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ImageResizerLambdaFunction
{
    public class Function
    {
        private static readonly IAmazonS3 _s3client = new AmazonS3Client();
        const string _destinationBucket = "<destination-bucket-name>";
        const int size = 150;
        const int quality = 75;
        
       public async Task<string> FunctionHandler(S3EventNotification s3event, ILambdaContext context)
        {
            try
            {
                LambdaLogger.Log($"Calling function name: {context.FunctionName}\\n");
                
                var record = s3event.Records[0];
                var srcBucket = record.S3.Bucket.Name;
                var key = record.S3.Object.Key;
                
                LambdaLogger.Log($"Bucket: {srcBucket} Key: {key}");

                var tempFilePath = "/tmp/" + key;
                var resizedFilePath = "/tmp/resized-" + key;

                await DownloadFileAsync(srcBucket, key, tempFilePath); // TODO: take into account the folder structure???               
            
                using (var image = new MagickImage(tempFilePath))
                {
                    image.Resize(size, size);
                    image.Strip();
                    image.Quality = quality;
                    image.Write(resizedFilePath);
                }

                await UploadFileAsync(_destinationBucket, resizedFilePath);
                
                return resizedFilePath;
            }
            catch(Exception ex)
            {
                LambdaLogger.Log($"Exception: {ex.Message}");
            }

            return string.Empty;
        }

        private static async Task DownloadFileAsync(string bucketName, string keyName, string tempFilePath)
        {
            //var responseBody = string.Empty;
            var cancellationTokensource = new CancellationTokenSource();
            var token = cancellationTokensource.Token;
            
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName
                };
                
                using (GetObjectResponse response = await _s3client.GetObjectAsync(request))
                using (Stream responseStream = response.ResponseStream)
                {
                    string title = response.Metadata["x-amz-meta-title"]; // Assume you have "title" as medata added to the object.
                    string contentType = response.Headers["Content-Type"];
                    //LambdaLogger.Log($"Object metadata, Title: {title}");
                    //LambdaLogger.Log($"Content type: {contentType}");

                    await response.WriteResponseStreamToFileAsync(tempFilePath, false, token);
                    LambdaLogger.Log($"Downloaded {tempFilePath}");
                }
            }
            catch (AmazonS3Exception e)
            {
                // If bucket or object does not exist
                LambdaLogger.Log($"Error encountered ***. Message:'{e.Message}' when reading object");
            }
            catch (Exception e)
            {
                LambdaLogger.Log($"Unknown encountered on server. Message:'{e.Message}' when reading object");
            }
        }

        private static async Task UploadFileAsync(string bucketName, string filePath)
        {
            try
            {
                var fileTransferUtility = new TransferUtility(_s3client);

                await fileTransferUtility.UploadAsync(filePath, bucketName);
                LambdaLogger.Log($"Uploaded {filePath} in {bucketName}");
            }
            catch (AmazonS3Exception e)
            {
                LambdaLogger.Log($"Error encountered on server. Message:'{e.Message}' when writing an object");
            }
            catch (Exception e)
            {
                LambdaLogger.Log($"Unknown encountered on server. Message:'{e.Message}' when writing an object");
            }
        }
    }
}
