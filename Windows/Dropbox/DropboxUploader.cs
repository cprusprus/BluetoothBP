using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;
using Windows.Media.Protection.PlayReady;

// Tools -> Nuget Package Manager -> Package Manager Console -> Install-Package Dropbox.Api

namespace BpMon
{
    public class DropboxUploader
    {
        public DropboxUploader()
        {
            Directory.CreateDirectory(m_appDataFilePath);
            m_refreshToken = LoadRefreshToken();
        }

        public async Task<string> GetAccessTokenAsync()
        {
            // First make sure we have an authorization code (user has granted the app access to their Dropbox account for the BpMon Apps folder).
            if (string.IsNullOrEmpty(m_authorizationCode))
            {
                // Switch to using DropboxOAuth2Helper.GetAuthorizeUri(m_clientId) if it stops giving token_access_type=legacy in the future, which results in short lived tokens and no refresh token
                var authorizeUri = new Uri($"https://www.dropbox.com/oauth2/authorize?response_type=code&client_id={m_clientId}&token_access_type=offline");
                DeleteRefreshToken();
                Process.Start(new ProcessStartInfo(authorizeUri.ToString()) { UseShellExecute = true });
                return "";
            }

            // If we don't have a refresh token yet, then we need to do the first leg of the OAuth2 flow
            if (string.IsNullOrEmpty(m_refreshToken))
            {
                var response = await DropboxOAuth2Helper.ProcessCodeFlowAsync(m_authorizationCode, m_clientId, m_clientSecret);

                m_accessToken = response.AccessToken;
                m_accessTokenExpiresAt = response.ExpiresAt ?? DateTime.MinValue;
                m_refreshToken = response.RefreshToken;
                SaveRefreshToken(m_refreshToken);
                return m_accessToken;
            }

            // If the current access token is still valid, then use it
            if (m_accessTokenExpiresAt > DateTime.UtcNow)
            {
                return m_accessToken;
            }

            // If we have a refresh token, then we can use it to get a new access token
            return await RefreshAccessTokenAsync();
        }

        private async Task<string> RefreshAccessTokenAsync()
        {
            using (var client = new HttpClient())
            {
                var requestUrl = $"https://api.dropbox.com/oauth2/token?refresh_token={m_refreshToken}&grant_type=refresh_token&client_id={m_clientId}&client_secret={m_clientSecret}";
                var response = await client.PostAsync(requestUrl, null);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(responseContent);
                var root = jsonDocument.RootElement;

                m_accessToken = root.GetProperty("access_token").GetString();
                var expiresIn = root.GetProperty("expires_in").GetInt32();
                m_accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
                return m_accessToken;
            }
        }

        private void SaveRefreshToken(string refreshToken)
        {
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var filePath = Path.Combine(m_appDataFilePath, m_refreshTokenFileName);
                File.WriteAllText(filePath, refreshToken);
            }
        }

        private string LoadRefreshToken()
        {
            var filePath = Path.Combine(m_appDataFilePath, m_refreshTokenFileName);
            return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
        }

        private void DeleteRefreshToken()
        {
            var filePath = Path.Combine(m_appDataFilePath, m_refreshTokenFileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public async Task<MemoryStream> CreateCsvMemoryStreamAsync(string[] headers, string[][] rows)
        {
            var memoryStream = new MemoryStream();
            using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, 1024, true))
            {
                // Write headers
                await writer.WriteLineAsync(string.Join(",", headers));

                // Write rows
                foreach (var row in rows)
                {
                    await writer.WriteLineAsync(string.Join(",", row));
                }

                await writer.FlushAsync();
            }

            // Reset the position of the memory stream to the beginning
            memoryStream.Position = 0;
            return memoryStream;
        }

        private async Task DeleteTimestampFile(DropboxClient client, string dropboxFolderPath, string dropboxFileName)
        {
            // Delete timestamp file which iOS Shortcuts uses to determine if new data is available (Shortcuts can no longer delete Dropbox files).
            // If timestamp file doesn't exist, then new data is available to sync to Apple Health. Shortcuts will create then timestamp file signifying last data sync time.
            bool timeStampFileExists = false;
            var folders = client.Files.ListFolderAsync(dropboxFolderPath);
            foreach (var file in folders.Result.Entries)
            {
                if (file.Name == dropboxFileName + ".timestamp.txt")
                {
                    timeStampFileExists = true;
                    break;
                }
            }
            if (timeStampFileExists)
            {
                var deleteResult = await client.Files.DeleteV2Async(dropboxFolderPath + "/" + dropboxFileName + ".timestamp.txt");
                Debug.WriteLine($"Deleted: {deleteResult.Metadata.Name}");
            }
        }

        public async Task<string> UploadCsvFileAsync(string[] headers, string[][] rows, string dropboxFolderPath, string dropboxFileName)
        {
            try
            {
                if (string.IsNullOrEmpty(m_clientId))
                {
                    return "";      // Must provide client ID and secret if want to use Dropbox upload & Apple Health syncing
                }

                var result = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(result))
                {
                    return "Now fill in m_authorizationCode with authorization code and rebuild";
                }

                using (var client = new DropboxClient(m_accessToken))
                {
                    using (var memoryStream = await CreateCsvMemoryStreamAsync(headers, rows))
                    {
                        var dropboxPath = $"{dropboxFolderPath}/{dropboxFileName}";
                        var uploadResult = await client.Files.UploadAsync(dropboxPath, WriteMode.Overwrite.Instance, body: memoryStream);
                        Debug.WriteLine($"Uploaded {uploadResult.Name} to {uploadResult.PathDisplay}");
                    }

                    await DeleteTimestampFile(client, dropboxFolderPath, dropboxFileName);
                }
                return "";
            }
            catch (OAuth2Exception e)
            {
                Debug.WriteLine($"Error calling Dropbox API: {e.Message}:{e.ErrorDescription}");
                return $"{e.Message}:{e.ErrorDescription}";
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Error: {e.Message}");
                return e.Message;
            }
        }

        private const string m_clientId = "";
        private const string m_clientSecret = "";
        // Fill in after granting access to the app (run this app once and then login to Dropbox). If you get an invalid_grant exception, make this empty string again and rebuild
        private const string m_authorizationCode = "";
        private const string m_refreshTokenFileName = "RefreshToken.txt";
        // Should resolve to something like C:\Users\<user>\AppData\Local\Packages\<PackageFamilyName>\LocalCache\Local\BpMon. See Package.appxmanifest for PackageFamilyName
        private readonly string m_appDataFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BpMon");
        private string m_accessToken;
        private DateTime m_accessTokenExpiresAt;
        private string m_refreshToken;
    }
}
