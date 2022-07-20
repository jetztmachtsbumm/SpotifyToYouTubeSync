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
            var playlist = await spotify.Playlists.Get(File.ReadAllText(applicationPath + "SpotifyPlaylist.ini"));

            //Create new YT-playlist if it doesn't exist yet, else get existing playlist
            var newPlaylist = new Playlist();
            newPlaylist.Snippet = new PlaylistSnippet();
            newPlaylist.Snippet.Title = playlist.Name;
            newPlaylist.Snippet.Description = playlist.Description;
            newPlaylist.Status = new PlaylistStatus();
            newPlaylist.Status.PrivacyStatus = "public";
            if (!File.Exists(applicationPath + "YTPlaylist.ini"))
            {
                //Create a new YT-playlist
                Console.WriteLine("Creating new YT-Playlist...");
                newPlaylist = await youtubeService.Playlists.Insert(newPlaylist, "snippet,status").ExecuteAsync();
                File.WriteAllText(applicationPath + "YTPlaylist.ini", newPlaylist.Id);
            }
            else
            {
                newPlaylist.Id = File.ReadAllText(applicationPath + "YTPlaylist.ini");
            }

            //Search video from spotify-playlist and add it to the YT-Playlist

            if (!File.Exists(applicationPath + "offset.ini"))
            {
                File.WriteAllText(applicationPath + "offset.ini", "0");
            }

            int offset = int.Parse(File.ReadAllText(applicationPath + "Offset.ini"));
            Paging<PlaylistTrack<IPlayableItem>> playlistTracks = await spotify.Playlists.GetItems(playlist.Id, new PlaylistGetItemsRequest { Offset = offset });
            foreach (PlaylistTrack<IPlayableItem> item in playlistTracks.Items)
            {
                if (item.Track is FullTrack track)
                {
                    string trackName = track.Artists[0].Name + " - " + track.Name;
                    Console.WriteLine("Moving: " + trackName + "...");

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
                        await youtubeService.PlaylistItems.Insert(newPlaylistItem, "snippet").ExecuteAsync();
                        offset++;
                        File.WriteAllText(applicationPath + "offset.ini", "" + offset);
                    }
                }
            }
        }
    }
}