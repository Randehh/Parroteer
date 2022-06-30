using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Parroteer.Utilities
{
    public static class GoogleDriveDownloader
    {

        public static async Task<MemoryStream> DownloadFile(string fileId, Action<float> onProgressUpdate) {
            try {
                GoogleCredential credential = GoogleCredential
                        .FromFile("parroteer-6b05e84c48c5.json")
                        .CreateScoped(DriveService.Scope.Drive);

                // Create Drive API service.
                var service = new DriveService(new BaseClientService.Initializer {
                    HttpClientInitializer = credential,
                    ApplicationName = "Drive API Snippets"
                });

                var request = service.Files.Get(fileId);
                request.Fields = "*";
                var file = await request.ExecuteAsync();
                var fileSize = file.Size;
                var stream = new MemoryStream();

                request.MediaDownloader.ProgressChanged +=
                    progress => {
                        switch (progress.Status) {
                            case DownloadStatus.Downloading: {
                                    Console.WriteLine(progress.BytesDownloaded);
                                    float progressStatus = (float)(progress.BytesDownloaded * 100 / fileSize) / 100;
                                    onProgressUpdate(progressStatus);
                                    break;
                                }
                        }
                    };
                await Task.Run(() => request.Download(stream));
                return stream;
            } catch (Exception e) {
                // TODO(developer) - handle error appropriately
                if (e is AggregateException) {
                    Console.WriteLine("Credential Not found");
                } else {
                    throw;
                }
            }
            return null;
        }
    }
}
