using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TeamCherry.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Silksong.ReturnToBench;

[HarmonyPatch]
[BepInAutoPlugin(id: "silksong.returntobench", name: "Return to Bench")]
public partial class ReturnToBenchPlugin : BaseUnityPlugin {
    private static ReturnToBenchPlugin instance = null!;
    private static ManualLogSource logger = null!;
    private static GameObject? returnButton;
    private static MenuButtonList.Entry? entry;
    private static bool returningToBench;
    private const string PAUSE_BENCH = "PAUSE_BENCH";

    private Harmony? harmony;

    private void Awake() {
        instance = this;
        logger = Logger;
        harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
    }

    private void Start() {
        MethodInfo original =
            AccessTools.Method(typeof(Language), nameof(Language.SwitchLanguage), [typeof(LanguageCode)]);
        HarmonyMethod postfix = new HarmonyMethod(typeof(ReturnToBenchPlugin), nameof(LanguageSwitchLanguage));
        harmony?.Patch(original, postfix: postfix);
    }

    private void OnDestroy() {
        harmony?.UnpatchSelf();
        if (returnButton) {
            Destroy(returnButton);
        }

        if (entry != null && UIManager._instance) {
            var menuButtonList = UIManager._instance.pauseMenuScreen.GetComponent<MenuButtonList>();
            var entries = menuButtonList.entries.ToList();
            entries.Remove(entry);
            menuButtonList.entries = entries.ToArray();
        }
    }

    private void Update() {
        if (!returnButton) {
            CreateReturnButton();
        }
        
        // 如果真的出现问题，通过回到首页重置该变量
        if (returningToBench && GameManager._instance && GameManager._instance.sceneName == "Menu_Title") {
            returningToBench = false;
        }
    }

    private static void CreateReturnButton() {
        var pauseMenuScreen = UIManager._instance?.pauseMenuScreen;
        if (!pauseMenuScreen) {
            return;
        }

        var controls = pauseMenuScreen.transform.Find("Container/Controls");
        var continueButton = controls?.Find("ContinueButton");
        var exitButton = controls?.Find("ExitButton");
        if (!continueButton || !exitButton) {
            return;
        }

        LanguageSwitchLanguage(Language._currentLanguage);

        returnButton = Instantiate(continueButton.gameObject, continueButton.parent);
        returnButton.name = "ReturnToBenchButton";
        returnButton.transform.SetSiblingIndex(exitButton.GetSiblingIndex());
        exitButton.transform.SetSiblingIndex(exitButton.GetSiblingIndex() + 1);

        var pauseMenuButton = returnButton.GetComponent<PauseMenuButton>();
        var menuButtonList = pauseMenuScreen.GetComponent<MenuButtonList>();
        entry = new MenuButtonList.Entry {
            selectable = pauseMenuButton
        };
        var entries = menuButtonList.entries.ToList();
        entries.Insert(entries.Count - 1, entry);
        menuButtonList.entries = entries.ToArray();

        var autoLocalizeTextUI = returnButton.GetComponentInChildren<AutoLocalizeTextUI>();
        autoLocalizeTextUI.TextSheet = Id;
        autoLocalizeTextUI.TextKey = PAUSE_BENCH;
    }

    // [HarmonyPatch(typeof(Language), nameof(Language.SwitchLanguage), typeof(LanguageCode))]
    // [HarmonyPostfix]
    // 在 Start 中手动 Hook，否则启动报错
    private static void LanguageSwitchLanguage(LanguageCode code) {
        var currentEntrySheets = Language._currentEntrySheets;
        currentEntrySheets?.TryAdd(Id, new Dictionary<string, string> {
            { PAUSE_BENCH, code == LanguageCode.ZH ? "返回长椅" : "Return to Bench" }
        });
    }

    [HarmonyPatch(typeof(PauseMenuButton), nameof(PauseMenuButton.OnSubmit))]
    [HarmonyPostfix]
    private static void PauseMenuButtonOnSubmit(PauseMenuButton __instance) {
        if (__instance.gameObject == returnButton && __instance.interactable && __instance.ih.PauseAllowed &&
            !UIManager.instance.ignoreUnpause) {
            returningToBench = true;
        } else {
            returningToBench = false;
        }
    }

    [HarmonyPatch(typeof(HeroController), nameof(HeroController.CanTakeDamage))]
    [HarmonyPostfix]
    private static void HeroControllerCanTakeDamage(ref bool __result) {
        if (returningToBench) {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.PauseGameToggleByMenu))]
    [HarmonyPostfix]
    private static IEnumerator GameManagerPauseGameToggleByMenu(IEnumerator __result, GameManager __instance) {
        while (__result.MoveNext()) {
            yield return __result.Current;
        }

        if (returningToBench) {
            StopAllSceneMusic();
            // 避免在蛆池中传送后灵丝UI异常；heroController.Respawn()方法会清除蛆虫状态
            HeroController.instance.cState.dead = true;
            __instance.needFirstFadeIn = true;
            __instance.ReadyForRespawn(isFirstLevelForPlayer: false);
        }
    }

    [HarmonyPatch(typeof(HeroController), nameof(HeroController.FinishedEnteringScene))]
    [HarmonyPostfix]
    private static void HeroControllerFinishedEnteringScene(HeroController __instance) {
        if (returningToBench) {
            returningToBench = false;
            __instance.proxyFSM.SendEvent("HeroCtrl-EnteringScene");
        }
    }

    private static void StopAllSceneMusic() {
        var gameManager = GameManager.instance;
        gameManager.AudioManager.StopAndClearMusic();
        gameManager.AudioManager.StopAndClearAtmos();
        var transform = gameManager.AudioManager.transform.Find("Music");
        if (!transform) {
            return;
        }

        var restArea = transform.Find("RestArea")?.GetComponent<PlayMakerFSM>();
        if (restArea) {
            restArea.fsm.Finished = false;
            restArea.SendEventSafe("REST AREA MUSIC STOP FAST");
        }

        var fleaCaravan = transform.Find("FleaCaravan")?.GetComponent<PlayMakerFSM>();
        if (fleaCaravan) {
            fleaCaravan.fsm.Finished = false;
            fleaCaravan.SendEventSafe("FLEA MUSIC STOP FAST");
        }
    }
}
