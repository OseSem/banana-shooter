
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

using CodingDaniel.MapEditor.MEEditor.MESave;
using Level;
using Menu;
using Multiplayer;
using Quest;
using SecureServer;
using Steamworks;
using Steamworks.NET;
using SteamWorkshop;
using UnityEngine;
using UnityEngine.SceneManagement;
using Web;
using Debug = UnityEngine.Debug;

namespace Manager
{
    [DefaultExecutionOrder(-110)]
    public class Preload : MonoBehaviour
    {
        public static Preload Instance { private set; get; }

        public static int Step = 0;

        private int _currentStep = 0;



        public enum LoadingState
        {
            Loading_Main_Game,
            Loading_Quests,
            Loading_Replay,
            Loading_Inventory,
            Loading_Steam_Workshop,
            Loading_Map_Editor,
            Loading_Roles,
            Loading_Level,
            Loading_Leaderboard,
            Authenticating,
        }

        [SerializeField] private PreloadMenu menu;
        [SerializeField] private float initializationTimeout = 60f;
        private bool _loadingFailed;
        public static bool Initialized = false;

        private void Awake()
        {

            Instance = this;
            Initialized = false;
            _loadingFailed = false;

            //GameManager -- 8
            //QuestManager -- 7
            //Load Level -- 1
            //LeaderBoard -- 9
            //Authenticating -- 1
            Step = 8 + 7 + 1 + 9 + 1;
        }

        public static void IncreaseTotalStep(int s)
        {
            Step += s;
        }

        public void NextStep(int step = 1)
        {
            _currentStep += step;

            menu.SetProgress(_currentStep, 0);
        }

        private IEnumerator Start()
        {
            if (!SteamManager.Initialized) yield break;

            Authenticate();
            RolesManager.Instance.TryToInitialize();
            LeaderboardManager.Instance.Refresh();

            Stopwatch stopwatch = Stopwatch.StartNew();

            yield return WaitForInitialization(() => GameManager.Initialized, LoadingState.Loading_Main_Game);
            if (_loadingFailed) yield break;

            yield return WaitForInitialization(() => QuestManager.Initialized, LoadingState.Loading_Quests);
            if (_loadingFailed) yield break;

            yield return WaitForInitialization(() => MapSaver.Initialized, LoadingState.Loading_Map_Editor);
            if (_loadingFailed) yield break;

            // menu.SetLoadingStateText(LoadingState.Loading_Inventory);
            //
            // while (!InventoryManager.Initialized)
            // {
            //     yield return null;
            // }

            yield return WaitForInitialization(() => SteamWorkshopManager.Initialized, LoadingState.Loading_Steam_Workshop);
            if (_loadingFailed) yield break;

            // Loading Level
            LevelManager.Instance.Refresh();
            yield return WaitForInitialization(() => LevelManager.Initialized, LoadingState.Loading_Level);
            if (_loadingFailed) yield break;


            // Requesting Roles
            yield return WaitForInitialization(() => RolesManager.Initialized, LoadingState.Loading_Roles);
            if (_loadingFailed) yield break;

            // Loading Leaderboard
            yield return WaitForInitialization(() => LeaderboardManager.Initialized, LoadingState.Loading_Leaderboard);
            if (_loadingFailed) yield break;

            yield return WaitForInitialization(() => Initialized, LoadingState.Authenticating);
            if (_loadingFailed) yield break;

            #region Load

            stopwatch.Stop();

            Debug.Log($"Load The Required Datas in {stopwatch.ElapsedMilliseconds / 1000f}s");

            TransitionUI.Instance.StartTransition();

            AsyncOperation operation = SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);

            operation.allowSceneActivation = false;

            while (operation.progress < 0.9f)
            {
                menu.SetProgress(_currentStep, operation.progress / Step);
                yield return null;
            }

            NextStep();

            yield return new WaitForSeconds(0.15f);

            #endregion

            operation.allowSceneActivation = true;

            TransitionUI.Instance.ClearTransition();
        }

        [Serializable]
        public class PlayerBansResponse
        {
            public List<PlayerBanSummary> players = new List<PlayerBanSummary>();

            PlayerBansResponse() { }
        }

        private IEnumerator WaitForInitialization(Func<bool> isReady, LoadingState state)
        {
            menu.SetLoadingStateText(state);
            float deadline = Time.realtimeSinceStartup + Math.Max(1f, initializationTimeout);
            while (!isReady())
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    _loadingFailed = true;
                    string error = $"Unable to finish loading {state}. Check Steam and your connection, then restart the game.";
                    Debug.LogError(error);
                    menu.ShowLoadingError(error);
                    yield break;
                }
                yield return null;
            }
        }

        async void Authenticate()
        {
            try
            {
                PlayerBansResponse response = await HttpClient.Get<PlayerBansResponse>(EndPoint.GetPlayerBansSummaries + SteamUser.GetSteamID(), false);
                PlayerBanSummary summary = response?.players?.Find(player => player != null) ?? new PlayerBanSummary();
                summary.Bans ??= new List<GameBan>();
                summary.Bans.RemoveAll(ban => ban == null);
                SteamManager.CurrentUserBanSummary = summary;
            }
            catch (Exception e)
            {
                Debug.LogError($"Authentication request failed: {e.Message}");
                SteamManager.CurrentUserBanSummary = new PlayerBanSummary();
            }
            finally
            {
                Initialized = true;
                if (!_loadingFailed && this != null) NextStep();
            }
        }
    }
}
