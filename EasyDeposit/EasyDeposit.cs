using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;
using Steamworks;
using HarmonyLib;
using UnityEngine.UI;
using HMLLibrary;
using RaftModLoader;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Linq;
using _EasyDeposit;

public class EasyDeposit : Mod
{
    static public JsonModInfo info;
    static public KeyCode key;
    static public string APIKey;
    public const string favoriteString = "Favored";
    public static bool ignoreHotbar = true;
    public static bool showNotify = true;
    public static float distance = 1;
    public static EasyDeposit instance;
    static string configPath = Path.Combine(SaveAndLoad.WorldPath, "EasyDeposit");
    bool Pdown = false;
    bool Pdown2 = false;
    Harmony harmony;
    public void Start()
    {
        harmony = new Harmony("com.aidanamite.EasyDeposit");
        harmony.PatchAll();
        info = modlistEntry.jsonmodinfo;
        instance = this;
        try
        {
            byte[] data = File.ReadAllBytes(configPath);
            key = (KeyCode)(data[0] * 256 + data[1]);
        }
        catch
        {
            key = KeyCode.Z;
        }
        if (RAPI.GetLocalPlayer() != null)
        {
            foreach (Slot slot in RAPI.GetLocalPlayer().Inventory.allSlots)
                slot.RefreshComponents();
            foreach (Storage_Small storage in StorageManager.allStorages)
                if (storage.GetInventoryReference() != null)
                    foreach (Slot slot in storage.GetInventoryReference().allSlots)
                        slot.RefreshComponents();
        }
        Log("Mod has been loaded!");
    }

    public void Update()
    {
        bool down = (ExtraSettingsAPI_Loaded && MyInput.GetButton(APIKey)) || Input.GetKey(key);
        if (CanvasHelper.ActiveMenu == MenuType.None && down && !Pdown)
        {
            Log(DepositAllItems());
        }
        Pdown = down;
        down = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (CanvasHelper.ActiveMenu == MenuType.Inventory && down && !Pdown2)
        {
            Slot hoverSlot = Traverse.Create(RAPI.GetLocalPlayer().Inventory).Field("hoverSlot").GetValue<Slot>();
            if (hoverSlot != null && hoverSlot.HasValidItemInstance() && hoverSlot.BelongsToPlayer())
            {
                if (!hoverSlot.itemInstance.TryGetData(out var data))
                    hoverSlot.itemInstance.exclusiveString = "";
                if (data.ContainsKey(favoriteString))
                    hoverSlot.itemInstance.RemoveValue(favoriteString);
                else
                    hoverSlot.itemInstance.SetValue(favoriteString, "");
                hoverSlot.RefreshComponents();
            }
        }
        Pdown2 = down;
    }

    public void OnModUnload()
    {
        harmony.UnpatchAll(harmony.Id);
        if (RAPI.GetLocalPlayer() != null)
        {
            foreach (Slot slot in RAPI.GetLocalPlayer().Inventory.allSlots)
                slot.GetComponent<Image>().color = Color.white;
            foreach (Storage_Small storage in StorageManager.allStorages)
                if (storage.GetInventoryReference() != null)
                    foreach (Slot slot in storage.GetInventoryReference().allSlots)
                        slot.GetComponent<Image>().color = Color.white;
        }
        Log("Mod has been unloaded!");
    }

    public static void LogTree(Transform transform)
    {
        Debug.Log(GetLogTree(transform));
    }

    public static string GetLogTree(Transform transform, string prefix = " -")
    {
        string str = "\n";
        if (transform.GetComponent<Behaviour>() == null)
            str += prefix + transform.name;
        else
            str += prefix + transform.name + ": " + transform.GetComponent<Behaviour>().GetType().Name;
        foreach (Transform sub in transform)
            str += GetLogTree(sub, prefix + "--");
        return str;
    }

    public static void LogWarning(object message)
    {
        Debug.LogWarning("[" + info.name + "]: " + message.ToString());
    }

    [ConsoleCommand(name: "fillNearbyChests", docs: "Deposits items from inventory to nearby storages")]
    public static string MyCommand(string[] args)
    {
        return DepositAllItems();
    }

    [ConsoleCommand(name: "depositKey", docs: "Gets or sets the key to be used for depositing items")]
    public static string MyCommand2(string[] args)
    {
        if (args.Length == 0)
            return key.ToString();
        if (args.Length > 1)
            return "Too many arguments";
        try
        {
            key = (KeyCode)Enum.Parse(typeof(KeyCode), args[0], true);
            if (ExtraSettingsAPI_Loaded)
                instance.ExtraSettingsAPI_SetKeybindMain("Deposit Key", key);
            try
            {
                File.WriteAllBytes(configPath, new byte[] { (byte)((int)key / 256), (byte)((int)key % 256) });
            }
            catch
            {
                LogWarning("Failed to save settings");
            }
            return "Key changed";
        }
        catch
        {
            return "Invalid key";
        }
    }

    [ConsoleCommand(name: "toggleIgnoreHotbar", docs: "Toggles whether or not the deposit will include the hotbar items")]
    public static string MyCommand3(string[] args)
    {
        ignoreHotbar = !ignoreHotbar;
        return "Deposit will" + (ignoreHotbar ? " not" : "") + " include hotbar items";
    }

    public static string DepositAllItems()
    {
        Network_Player player = RAPI.GetLocalPlayer();
        if (player == null)
            return "Must be used in world";
        Dictionary<string, int> deposits = new Dictionary<string, int>();
        Action<string, int> deposited = (x, y) =>
        {
            if (deposits.ContainsKey(x))
                deposits[x] += y;
            else
                deposits.Add(x, y);
        };
        List<Slot> playerItems = player.Inventory.allSlots;
        List<Slot> items = new List<Slot>();
        foreach (Slot slot in playerItems)
            if ((int)slot.slotType % 2 == 1 || (!ignoreHotbar && slot.slotType == SlotType.Hotbar))
            {
                items.Add(slot);
            }
        foreach (Storage_Small storage in StorageManager.allStorages)
        {
            Inventory container = storage.GetInventoryReference();
            if (storage.IsOpen || container == null || !Helper.LocalPlayerIsWithinDistance(storage.transform.position, player.StorageManager.maxDistanceToStorage * distance))
                continue;
            var edited = false;
            foreach (Slot slot in items)
                if (!slot.IsEmpty && container.GetItemCount(slot.GetItemBase()) > 0 && !(slot.itemInstance.TryGetData(out var data) && data.ContainsKey(favoriteString)))
                {
                    var p = slot.itemInstance.Amount;
                    container.AddItem(slot.itemInstance, false);
                    if (slot.itemInstance.Amount != p)
                    {
                        deposited(slot.itemInstance.UniqueName, slot.itemInstance.Amount - p);
                        edited = true;
                    }
                    if (slot.itemInstance.Amount == 0)
                        slot.SetItem(null);
                }
            if (edited)
            {
                var eventRef = Traverse.Create(ComponentManager<SoundManager>.Value).Field("eventRef_UI_MoveItem").GetValue<string>();
                storage.Close();
                var msg = new Message_SoundManager_PlayOneShot(Messages.SoundManager_PlayOneShot, ComponentManager<Raft_Network>.Value.NetworkIDManager, ComponentManager<SoundManager>.Value.ObjectIndex, eventRef, storage.transform.position);
                msg.Broadcast();
                FMODUnity.RuntimeManager.PlayOneShot(eventRef, msg.Position);
                if (showNotify)
                {
                    var pickup = Traverse.Create(player.Inventory).Field("inventoryPickup").GetValue<InventoryPickup>();
                    foreach (var item in deposits)
                        pickup.ShowItem(item.Key, item.Value);
                }
            }
        }
        return "Items deposited";
    }

    public void ExtraSettingsAPI_Load()
    {
        APIKey = ExtraSettingsAPI_GetKeybindName("Deposit Key");
        ExtraSettingsAPI_SettingsClose();
    }
    public void ExtraSettingsAPI_SettingsClose()
    {
        key = ExtraSettingsAPI_GetKeybindMain("Deposit Key");
        ignoreHotbar = ExtraSettingsAPI_GetCheckboxState("Ignore Hotbar Items");
        showNotify = ExtraSettingsAPI_GetCheckboxState("Show Deposit Notifications");
        distance = ExtraSettingsAPI_GetInputValue("distance").ParseFloat();
    }
    public void ExtraSettingsAPI_SettingsOpen()
    {
        ExtraSettingsAPI_SetKeybindMain("Deposit Key", key);
    }

    static bool ExtraSettingsAPI_Loaded = false;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public KeyCode ExtraSettingsAPI_GetKeybindMain(string SettingName) => default;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string ExtraSettingsAPI_GetKeybindName(string SettingName) => null;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string ExtraSettingsAPI_GetInputValue(string SettingName) => null;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool ExtraSettingsAPI_GetCheckboxState(string SettingName) => false;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ExtraSettingsAPI_SetKeybindMain(string SettingName, KeyCode value) { }
}

namespace _EasyDeposit
{
    static class ExtentionMethods
    {
        public static bool BelongsToPlayer(this Slot slot) => Traverse.Create(slot).Field("inventory").GetValue<Inventory>() is PlayerInventory;
        public static void Close(this Storage_Small box)
        {
            box.Close(RAPI.GetLocalPlayer());
            new Message_Storage_Close(Messages.StorageManager_Close, RAPI.GetLocalPlayer().StorageManager, box).Broadcast();
        }
        public static void Broadcast(this Message message) => ComponentManager<Raft_Network>.Value.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        public static void Send(this Message message, CSteamID steamID) => ComponentManager<Raft_Network>.Value.SendP2P(steamID, message, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        public static string String(this byte[] bytes, int length = -1, int offset = 0)
        {
            string str = "";
            if (length == -1)
                length = (bytes.Length - offset) / 2;
            while (str.Length < length)
            {
                str += BitConverter.ToChar(bytes, offset + str.Length * 2);
            }
            return str;

        }
        public static string String(this List<byte> bytes) => bytes.ToArray().String();
        public static byte[] Bytes(this string str)
        {
            var data = new List<byte>();
            foreach (char chr in str)
                data.AddRange(BitConverter.GetBytes(chr));
            return data.ToArray();
        }
        public static int Integer(this byte[] bytes, int offset = 0) => System.BitConverter.ToInt32(bytes, offset);
        public static byte[] Bytes(this int value) => BitConverter.GetBytes(value);


        public static void SetValue(this ItemInstance item, string valueName, string value)
        {
            int pos = 0;
            var data = item.exclusiveString.Bytes();
            var newData = new List<byte>(data);
            while (pos < data.Length)
            {
                var l = data.Integer(pos);
                var name = data.String(l, pos + 4);
                pos += 4 + name.Length * 2;
                l = data.Integer(pos);
                var oldValue = data.String(l, pos + 4);
                if (name == valueName)
                {
                    newData.RemoveRange(pos, 4 + oldValue.Length * 2);
                    newData.InsertRange(pos, value.Bytes());
                    newData.InsertRange(pos, value.Length.Bytes());
                    break;
                }
                pos += 4 + oldValue.Length * 2;
            }
            if (pos >= data.Length)
            {
                newData.AddRange(valueName.Length.Bytes());
                newData.AddRange(valueName.Bytes());
                newData.AddRange(value.Length.Bytes());
                newData.AddRange(value.Bytes());
            }
            item.exclusiveString = newData.String();
        }

        public static string GetValue(this ItemInstance item, string valueName)
        {
            int pos = 0;
            var data = item.exclusiveString.Bytes();
            while (pos < data.Length)
            {
                var l = data.Integer(pos);
                var name = data.String(l, pos + 4);
                pos += 4 + name.Length * 2;
                l = data.Integer(pos);
                var value = data.String(l, pos + 4);
                pos += 4 + value.Length * 2;
                if (name == valueName)
                    return value;
            }
            throw new MissingFieldException("No value \"" + valueName + "\" was found on the ItemInstance");
        }

        public static bool RemoveValue(this ItemInstance item, string valueName)
        {
            int pos = 0;
            var data = item.exclusiveString.Bytes();
            while (pos < data.Length)
            {
                var l = data.Integer(pos);
                var name = data.String(l, pos + 4);
                var offset = 4 + name.Length * 2;
                l = data.Integer(pos + offset);
                var value = data.String(l, pos + offset + 4);
                offset += 4 + value.Length * 2;
                if (name == valueName)
                {
                    var newData = new List<byte>(data);
                    newData.RemoveRange(pos, offset);
                    item.exclusiveString = newData.String();
                    return true;
                }
                pos += offset;
            }
            return false;
        }

        public static Dictionary<string, string> GetData(this ItemInstance item)
        {
            int pos = 0;
            var data = item.exclusiveString.Bytes();
            var d = new Dictionary<string, string>();
            while (pos < data.Length)
            {
                var l = data.Integer(pos);
                var name = data.String(l, pos + 4);
                pos += 4 + name.Length * 2;
                l = data.Integer(pos);
                var value = data.String(l, pos + 4);
                pos += 4 + value.Length * 2;
                d.Add(name, value);
            }
            return d;
        }

        public static bool TryGetData(this ItemInstance item, out Dictionary<string, string> values)
        {
            try
            {
                values = item.GetData();
                return true;
            }
            catch
            {
                values = new Dictionary<string, string>();
                return false;
            }
        }

        public static void SetData(this ItemInstance item, Dictionary<string, string> values)
        {
            var data = new List<byte>();
            foreach (var pair in values)
            {
                data.AddRange(pair.Key.Length.Bytes());
                data.AddRange(pair.Key.Bytes());
                data.AddRange(pair.Value.Length.Bytes());
                data.AddRange(pair.Value.Bytes());
            }
            item.exclusiveString = data.String();
        }
        public static float ParseFloat(this string value, float EmptyFallback = 1) => value.ParseNFloat() ?? EmptyFallback;
        public static float? ParseNFloat(this string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (value.Contains(",") && !value.Contains("."))
                value = value.Replace(',', '.');
            var c = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfoByIetfLanguageTag("en-NZ");
            Exception e = null;
            float r = 0;
            try
            {
                r = float.Parse(value);
            }
            catch (Exception e2)
            {
                e = e2;
            }
            CultureInfo.CurrentCulture = c;
            if (e != null)
                throw e;
            return r;
        }
    }

    [HarmonyPatch(typeof(Slot), "RefreshComponents")]
    public class Patch_RenderSlot
    {
        static void Postfix(ref Slot __instance)
        {
            Dictionary<string, string> data = null;
            if (__instance.HasValidItemInstance() && __instance.itemInstance.TryGetData(out data) && data.ContainsKey(EasyDeposit.favoriteString) && !__instance.BelongsToPlayer())
                __instance.itemInstance.exclusiveString = "";
            if (__instance.imageComponent != null)
                __instance.GetComponent<Image>().color = (__instance.HasValidItemInstance() && data.ContainsKey(EasyDeposit.favoriteString)) ? new Color(1, 1, 0.5f) : Color.white;
        }
    }

    [HarmonyPatch(typeof(RGD_Slot), "RestoreSlot")]
    public class Patch_RestoreSlot
    {
        static void Postfix(Slot slot)
        {
            if (slot.HasValidItemInstance() && slot.itemInstance.exclusiveString == EasyDeposit.favoriteString)
                slot.itemInstance.SetData(new Dictionary<string, string>() { [EasyDeposit.favoriteString] = "" });
        }
    }

    [HarmonyPatch]
    public class Patch_SlotSetItem
    {
        static IEnumerable<MethodBase> TargetMethods() => typeof(Slot).GetMethods().Where(x => x.Name == "SetItem" || x.Name == "AddItem");
        static void Postfix(ref Slot __instance)
        {
            if (__instance.HasValidItemInstance() && !__instance.BelongsToPlayer() && __instance.itemInstance.TryGetData(out var data) && data.ContainsKey(EasyDeposit.favoriteString))
                __instance.itemInstance.RemoveValue(EasyDeposit.favoriteString);
        }
    }

    [HarmonyPatch(typeof(Inventory), "MoveSlotToEmpty")]
    public class Patch_MoveSlot
    {
        static void Prefix(ref Slot fromSlot, int amount, ref bool __state)
        {
            __state = amount < (fromSlot.HasValidItemInstance() ? fromSlot.itemInstance.Amount : 0);
        }
        static void Postfix(ref Inventory __instance, ref Slot toSlot, ref bool __state)
        {
            if (toSlot.HasValidItemInstance() && toSlot.itemInstance.TryGetData(out var data) && data.ContainsKey(EasyDeposit.favoriteString) && __state)
            {
                toSlot.itemInstance.RemoveValue(EasyDeposit.favoriteString);
                toSlot.RefreshComponents();
            }
        }
    }

    [HarmonyPatch(typeof(InventoryPickupMenuItem), "SetItem")]
    class Patch_PickupNotify
    {
        static void Postfix(int amount, InventoryPickupMenuItem __instance)
        {
            if (amount < 0)
                __instance.amountTextComponent.text = amount.ToString();
        }
    }
}