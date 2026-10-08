#if UNITY_SERVER
using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Manager;
using MapEditor;
using Multiplayer;
using Multiplayer.Entity.Server;
using Newtonsoft.Json;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dedicated
{
    /// <summary>
    /// Entry point of the Dedicated Server build. The normal boot scene still loads every manager;
    /// this logs the server into Steam as a game server, starts Riptide and loads maps itself,
    /// which on a listen server is done by the host's client.
    /// </summary>
    public class DedicatedServer : MonoBehaviour
    {
        public static DedicatedServer Instance { get; private set; }

        const float UpdateCheckInterval = 300f;
        const float UpdateShutdownDelay = 180f;

        string _serverId = "MyServer";
        ushort _port = 27015;
        ushort _maxPlayers = 40;
        Config _config;

        readonly StringBuilder _consoleInput = new();
        bool _consoleOpen = true;

        Callback<SteamServersConnected_t> _steamConnected;
        Callback<SteamServerConnectFailure_t> _steamFailed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Instance = new GameObject(nameof(DedicatedServer)).AddComponent<DedicatedServer>();
            DontDestroyOnLoad(Instance.gameObject);
        }

        IEnumerator Start()
        {
            ParseArgs();
            _config = LoadConfig(Path.Combine(Directory.GetCurrentDirectory(), "Servers", _serverId, "Config.json"));

            // Managers come from the boot scene; GameManager also applies the client frame cap when it finishes.
            float deadline = Time.realtimeSinceStartup + 10f;
            yield return new WaitUntil(() => GameManager.Initialized || Time.realtimeSinceStartup > deadline);
            yield return new WaitUntil(() => NetworkServerManager.Instance != null && NetworkServerManager.Instance.Server != null);

            // No rendering, so without a cap the main loop spins a whole core.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.RoundToInt(1f / Time.fixedDeltaTime);
            Time.maximumDeltaTime = 0.1f;

            // Only round-based types have a server-side flow; the others need a host client.
            NetworkServerManager.SetServerType(_config.Game.ServerType == ServerType.KnockoutRound ? ServerType.KnockoutRound : ServerType.Normal);
            NetworkServerManager.ServerRandomMap = _config.Game.RandomMap;
            NetworkServerManager.ServerRandomGameMode = _config.Game.RandomGameMode;
            NetworkServerManager.MinimalPlayerCount = _config.Game.MinimalPlayerAmount;
            NetworkServerManager.DlcOnly = _config.Server.DlcOnly;

            if (!StartSteam())
            {
                Application.Quit(1);
                yield break;
            }

            RolesManager.Instance.TryToInitialize();

            NetworkServerManager.ClientData.Clear();
            NetworkServerManager.SetIsPlaying(false);
            NetworkServerManager.Instance.Server.Start(_port, _maxPlayers, NetworkManager.PlayerHostedDemoMessageHandlerGroupId);

            Debug.Log($"[Dedicated] '{_config.Browser.ServerName}' listening on port {_port}, {_maxPlayers} slots");

            if (_config.Server.UpdateRestart)
                InvokeRepeating(nameof(CheckForUpdate), UpdateCheckInterval, UpdateCheckInterval);
        }

        bool StartSteam()
        {
            var mode = _config.Server.VacSecure ? EServerMode.eServerModeAuthenticationAndSecure : EServerMode.eServerModeAuthentication;
            if (!GameServer.Init(0, _port, (ushort)(_port + 1), mode, Application.version))
            {
                Debug.LogError("[Dedicated] GameServer.Init failed. Is steam_appid.txt next to the executable and the port free?");
                return false;
            }

            SteamGameServer.SetModDir("Banana Shooter");
            SteamGameServer.SetProduct("1949740");
            SteamGameServer.SetGameDescription("Banana Shooter");
            SteamGameServer.SetDedicatedServer(true);
            SteamGameServer.SetServerName(_config.Browser.ServerName);
            // The server browser reads these by position: server type; workshop; DLC only; description.
            SteamGameServer.SetGameTags($"{(int)NetworkServerManager.ServerType};0;{(_config.Server.DlcOnly ? 1 : 0)};{_config.Browser.DescriptionShort}");
            SteamGameServer.SetMaxPlayerCount(_maxPlayers);

            _steamConnected = Callback<SteamServersConnected_t>.CreateGameServer(_ => Debug.Log($"[Dedicated] Logged in to Steam as {SteamGameServer.GetSteamID()}"));
            _steamFailed = Callback<SteamServerConnectFailure_t>.CreateGameServer(r => Debug.LogError($"[Dedicated] Steam login failed: {r.m_eResult}"));

            if (string.IsNullOrEmpty(_config.Server.LoginToken))
                SteamGameServer.LogOnAnonymous();
            else
                SteamGameServer.LogOn(_config.Server.LoginToken);

            SteamGameServer.SetAdvertiseServerActive(true);
            return true;
        }

        void Update()
        {
            if (_steamConnected != null)
                GameServer.RunCallbacks();

            PollConsole();
        }

        void OnApplicationQuit()
        {
            NetworkServerManager.Instance.StopServer();
            SteamGameServer.SetAdvertiseServerActive(false);
            SteamGameServer.LogOff();
            GameServer.Shutdown();
        }

        /// <summary>Called by NetworkServerManager right after it tells the clients which map to load.</summary>
        public void LoadMap() => StartCoroutine(LoadMapRoutine());

        IEnumerator LoadMapRoutine()
        {
            var server = NetworkServerManager.Instance;
            ClearPlayers();

            // Game modes enable themselves by comparing against the client-side mode.
            NetworkManager.ClientGameMode = NetworkServerManager.ServerGameMode;
            SteamGameServer.SetMapName(server.CurrentMap);

            yield return SceneManager.LoadSceneAsync(server.CurrentMap);

            server.SetGameState(GameState.Warmup);

            var game = GameModes.Create(NetworkServerManager.ServerGameMode, MapBound.Instance.gameObject);
            server.game = game;
            NetworkManager.Instance.game = game;

            server.StartGameFromLoad();
        }

        /// <summary>On a listen server the host's scene reset does this when voting starts or a map loads.</summary>
        public static void ClearPlayers()
        {
            foreach (var player in ServerPlayer.list.Values)
                if (player != null) player.Destroy();
            ServerPlayer.list.Clear();
            GameManager.Entities.Clear();
        }

        void CheckForUpdate()
        {
            if (!SteamGameServer.WasRestartRequested()) return;

            CancelInvoke(nameof(CheckForUpdate));
            float delay = NetworkServerManager.Instance.Server.ClientCount == 0 ? 0f : UpdateShutdownDelay;
            Debug.Log($"[Dedicated] Steam requested a restart for a game update, shutting down in {delay}s");
            Invoke(nameof(Quit), delay);
        }

        void Quit() => Application.Quit();

        // A thread blocked in Console.ReadLine keeps IL2CPP from finishing shutdown while stdin is open
        // (terminal, docker -it), so stdin is polled on the main thread instead.
#if UNITY_STANDALONE_LINUX
        [StructLayout(LayoutKind.Sequential)]
        struct PollFd
        {
            public int Fd;
            public short Events;
            public short Revents;
        }

        const short PollIn = 1;

        [DllImport("libc.so.6", EntryPoint = "poll")]
        static extern int Poll(ref PollFd fds, uint count, int timeoutMs);

        [DllImport("libc.so.6", EntryPoint = "read")]
        static extern IntPtr Read(int fd, byte[] buffer, IntPtr count);

        readonly byte[] _readBuffer = new byte[1024];

        void PollConsole()
        {
            var stdin = new PollFd { Fd = 0, Events = PollIn };
            while (_consoleOpen && Poll(ref stdin, 1, 0) > 0)
            {
                int read = (int)Read(0, _readBuffer, (IntPtr)_readBuffer.Length);
                if (read <= 0)
                {
                    _consoleOpen = false;
                    return;
                }

                _consoleInput.Append(Encoding.UTF8.GetString(_readBuffer, 0, read));
                for (int newline; (newline = _consoleInput.ToString().IndexOf('\n')) >= 0;)
                {
                    RunCommand(_consoleInput.ToString(0, newline));
                    _consoleInput.Remove(0, newline + 1);
                }
            }
        }
#else
        void PollConsole() { }
#endif

        void RunCommand(string line)
        {
            string[] args = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0) return;

            var server = NetworkServerManager.Instance;
            switch (args[0].ToLowerInvariant())
            {
                case "status":
                    Debug.Log($"[Dedicated] {NetworkServerManager.GameState}, map '{server.CurrentMap}', {server.Server.ClientCount}/{_maxPlayers} players");
                    foreach (var data in NetworkServerManager.ClientData.Values)
                        Debug.Log($"  {data.Id}  {data.SteamId}  {data.Name}");
                    break;
                case "kick" when args.Length > 1 && ushort.TryParse(args[1], out var id):
                    server.Server.DisconnectClient(id, NetworkServerManager.GetDisconnectMessage("Kicked"));
                    break;
                case "start":
                    server.ForceStart();
                    break;
                case "quit":
                    Application.Quit();
                    break;
                default:
                    Debug.Log("[Dedicated] Commands: status, kick <id>, start, quit");
                    break;
            }
        }

        void ParseArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                string value = args[i + 1];
                switch (args[i].ToLowerInvariant())
                {
                    case "+server":
                        _serverId = value;
                        break;
                    case "+port":
                        if (ushort.TryParse(value, out var port)) _port = port;
                        break;
                    case "+maxplayercount":
                        if (ushort.TryParse(value, out var max)) _maxPlayers = (ushort)Mathf.Clamp(max, 2, 80);
                        break;
                }
            }
        }

        static Config LoadConfig(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    return JsonConvert.DeserializeObject<Config>(File.ReadAllText(path)) ?? new Config();
                }
                catch (JsonException e)
                {
                    Debug.LogError($"[Dedicated] {path} is invalid, using defaults: {e.Message}");
                    return new Config();
                }
            }

            var config = new Config();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
            Debug.Log($"[Dedicated] Wrote default config to {path}");
            return config;
        }

        // Same keys as the old dedicated server's Config.json, so existing files keep working.
        class Config
        {
            [JsonProperty("Server")] public ServerSection Server = new();
            [JsonProperty("Browser")] public BrowserSection Browser = new();
            [JsonProperty("Game")] public GameSection Game = new();

            public class ServerSection
            {
                [JsonProperty("Login_Token")] public string LoginToken = "";
                [JsonProperty("VAC_Secure")] public bool VacSecure = true;
                [JsonProperty("DLC_Only")] public bool DlcOnly;
                [JsonProperty("Enable_Update_Restart")] public bool UpdateRestart = true;
            }

            public class BrowserSection
            {
                [JsonProperty("Server_Name")] public string ServerName = "Banana Shooter";
                [JsonProperty("Description_Short")] public string DescriptionShort = "";
            }

            public class GameSection
            {
                [JsonProperty("Random_Map")] public bool RandomMap;
                [JsonProperty("Random_GameMode")] public bool RandomGameMode;
                [JsonProperty("Minimal_Player_Amount")] public ushort MinimalPlayerAmount = 2;
                [JsonProperty("Server_Type")] public ServerType ServerType = ServerType.Normal;
            }
        }
    }
}
#endif
