using Google.Apis.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Util.Store;
using SpotifyAPI.Web;

namespace SpotifyToYouTubeSync
{
    internal class Program
    {

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                new Program().Run().Wait();
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.InnerExceptions)
                {
                    Console.WriteLine("Error: " + e.Message);
                }
            }
        }

        private async Task Run()
        {
            string applicationPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            //Log into YT-Account
            Console.WriteLine("Logging into YT-Account...");
            UserCredential credential;
            using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    new[] { YouTubeService.Scope.Youtube },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(GetType().ToString())
                );
            }

            //Init YT-Service
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = GetType().ToString()
            });

            //Init Spotify-Client and get playlist
            Console.WriteLine("Connecting to spotify...");
            var config = SpotifyClientConfig.CreateDefault();
            var request = new ClientCredentialsRequest("b06473b5c7c549ffaa969475426f8123", "7e561525a7dc4b07a0c50bd76601a266");
            var response = await new OAuthClient(config).RequestToken(request);
            var spotify = new SpotifyClient(config.WithToken(response.AccessToken));
            if(!File.Exists(applicationPath + "SpotifyPlaylist.ini"))
            {
                File.Create(applicationPath + "SpotifyPlaylist.ini");
                Console.WriteLine("Specify spotify-playlist in SpotifyPlaylist.ini");
                return;
            }

            //Delete old YT-playlist
            if (File.Exists(applicationPath + "YTPlaylist.ini"))
            {
                Console.WriteLine("Deleting old YT-Playlist...");
                string playlistID = File.ReadAllText(applicationPath + "YTPlaylist.ini");
                await youtubeService.Playlists.Delete(playlistID).ExecuteAsync();
                File.Delete(applicationPath + "YTPlaylist.ini");
            }

            
            //Create a new YT-playlist
            Console.WriteLine("Creating new YT-Playlist...");
            var newPlaylist = new Playlist();
            newPlaylist.Snippet = new PlaylistSnippet();
            newPlaylist.Snippet.Title = "Test Playlist";
            newPlaylist.Snippet.Description = "Test Description";
            newPlaylist.Status = new PlaylistStatus();
            newPlaylist.Status.PrivacyStatus = "public";
            newPlaylist = await youtubeService.Playlists.Insert(newPlaylist, "snippet,status").ExecuteAsync();
            File.WriteAllText(applicationPath + "YTPlaylist.ini", newPlaylist.Id);
            

            //Search video from spotify-playlist and add it to the YT-Playlist
            var playlist = await spotify.Playlists.Get(File.ReadAllText(applicationPath + "SpotifyPlaylist.ini"));

            for(int i = 0; i < playlist.Tracks.Total / 100 + 2; i++)
            {
                Paging<PlaylistTrack<IPlayableItem>> playlistTracks = await spotify.Playlists.GetItems(playlist.Id, new PlaylistGetItemsRequest { Offset = i * 100 });
                foreach (PlaylistTrack<IPlayableItem> item in playlistTracks.Items)
                {
                    if (item.Track is FullTrack track)
                    {
                        string trackName = track.Artists[0].Name + " - " + track.Name;
                        Console.WriteLine("Adding: " + trackName + "...");

                        var searchListRequest = youtubeService.Search.List("snippet");
                        searchListRequest.Q = trackName;
                        searchListRequest.MaxResults = 1;

                        var searchListResponse = await searchListRequest.ExecuteAsync();

                        if (searchListResponse.Items[0].Id.Kind.Equals("youtube#video"))
                        {
                            var newPlaylistItem = new PlaylistItem();
                            newPlaylistItem.Snippet = new PlaylistItemSnippet();
                            newPlaylistItem.Snippet.PlaylistId = newPlaylist.Id;
                            newPlaylistItem.Snippet.ResourceId = new ResourceId();
                            newPlaylistItem.Snippet.ResourceId.Kind = "youtube#video";
                            newPlaylistItem.Snippet.ResourceId.VideoId = searchListResponse.Items[0].Id.VideoId;
                            newPlaylistItem = await youtubeService.PlaylistItems.Insert(newPlaylistItem, "snippet").ExecuteAsync();
                        }
                    }
                }
            }
        }
    }
}