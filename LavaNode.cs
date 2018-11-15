using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using Victoria.Entities;
using Victoria.Entities.Enums;
using Victoria.Entities.Payloads;
using Victoria.Entities.Stats;
using Victoria.Utilities;

namespace Victoria
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class LavaNode
    {
        /// <summary>
        /// 
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public Func<LogMessage, Task> Log;

        /// <summary>
        /// 
        /// </summary>
        public Func<Server, Task> StatsUpdated;

        /// <summary>
        /// 
        /// </summary>
        public Func<int, string, bool, Task> SocketClosed;

        /// <summary>
        /// 
        /// </summary>
        public Func<LavaPlayer, LavaTrack, long, Task> TrackStuck;

        /// <summary>
        /// 
        /// </summary>
        public Func<LavaPlayer, LavaTrack, string, Task> TrackException;

        /// <summary>
        /// 
        /// </summary>
        public Func<LavaPlayer, LavaTrack, TimeSpan, Task> PlayerUpdated;

        /// <summary>
        /// 
        /// </summary>
        public Func<LavaPlayer, LavaTrack, TrackReason, Task> TrackFinished;


        internal Sockeon _sockeon;
        private HttpClient _httpClient;
        private SocketVoiceState _socketVoiceState;
        private readonly Configuration _configuration;
        private readonly BaseDiscordClient _baseDiscordClient;
        private readonly ConcurrentDictionary<ulong, LavaPlayer> _players;

        internal LavaNode(string name, BaseDiscordClient baseDiscordClient, Configuration configuration)
        {
            Name = name;
            _baseDiscordClient = baseDiscordClient;
            _configuration = configuration;
            _players = new ConcurrentDictionary<ulong, LavaPlayer>();

            switch (baseDiscordClient)
            {
                case DiscordSocketClient socketClient:
                    socketClient.Disconnected += OnSocketDisconnected;
                    socketClient.VoiceServerUpdated += OnVoiceServerUpdated;
                    socketClient.UserVoiceStateUpdated += OnVoiceStateUpdated;
                    break;

                case DiscordShardedClient shardedClient:
                    shardedClient.ShardDisconnected += OnShardDisconnected;
                    shardedClient.VoiceServerUpdated += OnVoiceServerUpdated;
                    shardedClient.UserVoiceStateUpdated += OnVoiceStateUpdated;
                    break;
            }

            Initialize(configuration);
            IsConnected = true;
        }

        internal Task StartAsync()
            => _sockeon.ConnectAsync();

        internal async Task StopAsync()
        {
            foreach (var player in _players.Values)
                await player.DisconnectAsync().ConfigureAwait(false);

            await _sockeon.DisconnectAsync().ConfigureAwait(false);
            _sockeon.Dispose();
            _httpClient.Dispose();
            IsConnected = false;
        }

        internal void Initialize(Configuration configuration)
        {
            _sockeon = new Sockeon(Name, configuration);
            _sockeon.OnClose += () =>
            {
                Log(LogResolver.Info(_sockeon.Name, "Connection established."));
                return true;
            };
            _sockeon.OnOpen += () =>
            {
                Log(LogResolver.Info(_sockeon.Name, "Connection established."));
                return true;
            };
            _sockeon.OnMessage += OnMessage;
            _httpClient = new HttpClient(new HttpClientHandler
            {
                UseCookies = false,
                Proxy = configuration.Proxy,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                BaseAddress = new Uri($"http://{configuration.Host}:{configuration.Port}/loadtracks?identifier=")
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Victoria");
            _httpClient.DefaultRequestHeaders.Add("Authorization", configuration.Authorization);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="voiceChannel"></param>
        /// <param name="messageChannel"></param>
        /// <returns></returns>
        public async Task<LavaPlayer> ConnectAsync(IVoiceChannel voiceChannel, IMessageChannel messageChannel)
        {
            if (_players.TryGetValue(voiceChannel.GuildId, out var player))
                return player;

            player = new LavaPlayer(this, voiceChannel, messageChannel);
            await voiceChannel
                .ConnectAsync(_configuration.VoiceChannelOptions.SelfDeaf, _configuration.VoiceChannelOptions.SelfMute,
                    _configuration.VoiceChannelOptions.External)
                .ConfigureAwait(false);
            _players.TryAdd(voiceChannel.GuildId, player);
            return player;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="guildId"></param>
        /// <returns></returns>
        public LavaPlayer GetPlayer(ulong guildId)
            => _players.TryGetValue(guildId, out var player) ? player : null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public async Task<LavaResult> GetTracksAsync(string query)
        {
            query = WebUtility.UrlEncode(query);
            return await ResolveRequestAsync(query).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public async Task<LavaResult> SearchYoutubeAsync(string query)
        {
            query = WebUtility.UrlEncode($"ytsearch:{query}");
            return await ResolveRequestAsync(query).ConfigureAwait(false);
        }

        private bool OnMessage(string data)
        {
            var parsed = JObject.Parse(data);
            ulong guildId = 0;
            if (parsed.TryGetValue("guildId", out var value))
                guildId = ulong.Parse($"{value}");

            var opCode = $"{parsed.GetValue("op")}";
            switch (opCode)
            {
                case "playerUpdate":
                    var state = parsed.GetValue("state").ToObject<LavaState>();
                    UpdatePlayerInfo(guildId, state);
                    break;

                case "stats":
                    var server = parsed.ToObject<Server>();
                    StatsUpdated(server);
                    break;

                case "event":
                    var evt = parsed.GetValue("type").ToObject<EventType>();
                    var track = TrackResolver.DecodeTrack($"{parsed.GetValue("track")}");
                    switch (evt)
                    {
                        case EventType.TrackEndEvent:
                            var trackReason = parsed.GetValue("reason").ToObject<TrackReason>();
                            TrackUpdateInfo(guildId, track, trackReason);
                            break;

                        case EventType.TrackExceptionEvent:
                            var error = $"{parsed.GetValue("error")}";
                            TrackExceptionInfo(guildId, track, error);
                            break;

                        case EventType.TrackStuckEvent:
                            var threshold = long.Parse($"{parsed.GetValue("thresholdMs")}");
                            TrackStuckInfo(guildId, track, threshold);
                            break;

                        case EventType.WebSocketClosedEvent:
                            var reason = $"{parsed.GetValue("reason")}";
                            var code = int.Parse($"{parsed.GetValue("code")}");
                            var byRemote = bool.Parse($"{parsed.GetValue("byRemote")}");
                            SocketClosed(code, reason, byRemote);
                            break;

                        default:
                            Log(LogResolver.Info(Name, $"Unhandled event type: {evt}."));
                            break;
                    }

                    break;

                default:
                    Log(LogResolver.Info(Name, $"Unhandled OP code: {opCode}."));
                    break;
            }

            return true;
        }

        private async Task<LavaResult> ResolveRequestAsync(string query)
        {
            string json;
            using (var req = await _httpClient.GetAsync(query).ConfigureAwait(false))
            using (var res = await req.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var sr = new StreamReader(res, Encoding.UTF8))
                json = await sr.ReadToEndAsync().ConfigureAwait(false);
            var data = JToken.Parse(json);
            var tracks = new HashSet<LavaTrack>();

            switch (data)
            {
                case JArray jArray:
                    foreach (var arr in jArray)
                    {
                        var arrTrack = arr["info"].ToObject<LavaTrack>();
                        arrTrack.TrackString = $"{arr["track"]}";
                        tracks.Add(arrTrack);
                    }

                    return new LavaResult
                    {
                        Tracks = tracks,
                        PlaylistInfo = default,
                        LoadResultType = tracks.Count == 0 ? LoadResultType.LoadFailed : LoadResultType.TrackLoaded
                    };

                case JObject jObject:
                    var jsonArray = jObject["tracks"] as JArray;
                    var info = jObject.ToObject<LavaResult>();
                    foreach (var item in jsonArray)
                    {
                        var track = item["info"].ToObject<LavaTrack>();
                        track.TrackString = $"{item["track"]}";
                        tracks.Add(track);
                    }

                    info.Tracks = tracks;
                    return info;

                default:
                    return null;
            }
        }

        // Event stuff

        private void UpdatePlayerInfo(ulong guildId, LavaState state)
        {
            if (!_players.TryGetValue(guildId, out var old)) return;
            old.CurrentTrack.Position = state.Position;
            old.LastUpdate = state.Time;
            _players.TryUpdate(guildId, old, old);
            PlayerUpdated(old, old.CurrentTrack, state.Position);
        }

        private void TrackUpdateInfo(ulong guildId, LavaTrack track, TrackReason reason)
        {
            Log(LogResolver.Debug(Name, "Track update received."));
            if (!_players.TryGetValue(guildId, out var old)) return;
            if (reason != TrackReason.Replaced)
                old.CurrentTrack = default;
            _players.TryUpdate(guildId, old, old);
            TrackFinished(old, track, reason);
        }

        private void TrackStuckInfo(ulong guildId, LavaTrack track, long threshold)
        {
            Log(LogResolver.Debug(Name, $"{track.Title} timed out after {threshold}ms."));
            if (!_players.TryGetValue(guildId, out var old)) return;
            old.CurrentTrack = track;
            _players.TryUpdate(guildId, old, old);
            TrackStuck(old, track, threshold);
        }

        private void TrackExceptionInfo(ulong guildId, LavaTrack track, string reason)
        {
            Log(LogResolver.Debug(Name, $"{track.Title} threw an exception because {reason}."));
            if (!_players.TryGetValue(guildId, out var old)) return;
            old.CurrentTrack = track;
            _players.TryUpdate(guildId, old, old);
            TrackException(old, track, reason);
        }

        private async Task OnSocketDisconnected(Exception exception)
        {
            foreach (var player in _players.Values)
                await player.DisconnectAsync().ConfigureAwait(false);
            _players.Clear();
            await Log(LogResolver.Error(Name, "Socket disconnected.", exception)).ConfigureAwait(false);
        }

        private async Task OnShardDisconnected(Exception exception, DiscordSocketClient socketClient)
        {
            foreach (var guild in socketClient.Guilds)
            {
                if (!_players.ContainsKey(guild.Id)) continue;
                await _players[guild.Id].DisconnectAsync().ConfigureAwait(false);
                _players.TryRemove(guild.Id, out _);
            }

            await Log(LogResolver.Error(Name, "Discord shard lost connection.", exception)).ConfigureAwait(false);
        }

        private async Task OnVoiceServerUpdated(SocketVoiceServer socketVoiceServer)
        {
            if (!socketVoiceServer.Guild.HasValue || !_players.TryGetValue(socketVoiceServer.Guild.Id, out _))
                return;
            var voiceUpdate = new VoicePayload(socketVoiceServer, _socketVoiceState);
            await _sockeon.SendPayloadAsync(voiceUpdate).ConfigureAwait(false);
            await Log(LogResolver.Debug(Name, $"Sent voice server payload. {voiceUpdate}")).ConfigureAwait(false);
        }

        private async Task OnVoiceStateUpdated(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            if (user.Id != _baseDiscordClient.CurrentUser.Id) return;

            switch (oldState)
            {
                case var state when state.VoiceChannel is null && !(newState.VoiceChannel is null):
                    _socketVoiceState = newState;
                    await Log(LogResolver.Debug(Name, $"Socket voice state updated. {newState}")).ConfigureAwait(false);
                    break;

                case var state when !(state.VoiceChannel is null) && newState.VoiceChannel is null:
                    if (!_players.TryGetValue(state.VoiceChannel.Guild.Id, out var oldPlayer))
                        return;
                    await oldPlayer.DisconnectAsync().ConfigureAwait(false);
                    _players.TryRemove(state.VoiceChannel.Guild.Id, out _);
                    var payload = new DestroyPayload(state.VoiceChannel.Guild.Id);
                    await _sockeon.SendPayloadAsync(payload).ConfigureAwait(false);
                    await Log(LogResolver.Debug(Name, $"Sent destroy payload. {payload}")).ConfigureAwait(false);
                    break;

                case var state when state.VoiceChannel?.Id != newState.VoiceChannel?.Id:
                    if (!_players.TryGetValue(state.VoiceChannel.Guild.Id, out var updatePlayer))
                        return;
                    updatePlayer.VoiceChannel = newState.VoiceChannel;
                    _players.TryUpdate(state.VoiceChannel.Guild.Id, updatePlayer, updatePlayer);
                    await Log(LogResolver.Debug(Name, "Voice channel moved.")).ConfigureAwait(false);
                    break;
            }
        }

        internal void Dispose()
        {
            IsConnected = false;
            _players.Clear();
            _sockeon.Dispose();
            _httpClient.Dispose();
        }
    }
}