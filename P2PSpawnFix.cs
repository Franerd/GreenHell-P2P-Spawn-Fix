using System;
using System.Collections;
using System.Reflection;
using System.Threading;

using HarmonyLib;
using UnityEngine;

public class P2PSpawnFix : Mod
{
    private const string HarmonyId = "com.franerd.greenhell.p2pspawnfix";
    private Harmony _harmony;

    public void Start()
    {
        P2PSpawnFixRuntime.Reset();

        _harmony = new Harmony(HarmonyId);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        Debug.Log("[P2P Spawn Fix] Mod loaded. Network spawn protection is active.");
    }

    [ConsoleCommand("p2pfix", "Shows the P2P Spawn Fix status and counters")]
    public static void Command(string[] args)
    {
        if (args != null && args.Length > 0 &&
            !string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log("[P2P Spawn Fix] Use: p2pfix or p2pfix status");
            return;
        }

        Debug.Log(P2PSpawnFixRuntime.GetStatus());
    }

    public void OnModUnload()
    {
        if (_harmony != null)
        {
            _harmony.UnpatchAll(HarmonyId);
        }

        P2PSpawnFixRuntime.Reset();
        Debug.Log("[P2P Spawn Fix] Mod unloaded.");
    }
}

internal static class P2PSpawnFixRuntime
{
    [ThreadStatic]
    private static int _spawnDeserializeDepth;

    [ThreadStatic]
    private static int _objectSpawnDepth;

    [ThreadStatic]
    private static bool _currentSpawnHadNullArrayRepair;

    private static int _nullArraysRepaired;
    private static int _invalidSpawnsBlocked;
    private static int _emptyInitialStatesSkipped;
    private static int _sensitiveInitialStatesPreserved;
    private static int _playerStatesRepaired;
    private static int _playerResolvesDeferred;
    private static int _invalidPotteryGhostsBlocked;

    internal static bool IsInsideSpawnDeserialize
    {
        get { return _spawnDeserializeDepth > 0; }
    }

    internal static bool CanSkipCurrentEmptyInitialState
    {
        get
        {
            return
                _objectSpawnDepth > 0 &&
                _currentSpawnHadNullArrayRepair;
        }
    }

    internal static void EnterObjectSpawn()
    {
        if (_objectSpawnDepth == 0)
        {
            _currentSpawnHadNullArrayRepair = false;
        }

        _objectSpawnDepth++;
    }

    internal static void ExitObjectSpawn()
    {
        if (_objectSpawnDepth > 0)
        {
            _objectSpawnDepth--;
        }

        if (_objectSpawnDepth == 0)
        {
            _currentSpawnHadNullArrayRepair = false;
        }
    }

    internal static void EnterSpawnDeserialize()
    {
        _spawnDeserializeDepth++;
    }

    internal static void ExitSpawnDeserialize()
    {
        if (_spawnDeserializeDepth > 0)
        {
            _spawnDeserializeDepth--;
        }
    }

    internal static void RecordNullArrayRepair()
    {
        if (_objectSpawnDepth > 0)
        {
            _currentSpawnHadNullArrayRepair = true;
        }

        int count = Interlocked.Increment(ref _nullArraysRepaired);
        LogRateLimited(
            count,
            "[P2P Spawn Fix] Replaced a null spawn-data array with an empty array.");
    }

    internal static void RecordBlockedSpawn()
    {
        int count = Interlocked.Increment(ref _invalidSpawnsBlocked);
        LogRateLimited(
            count,
            "[P2P Spawn Fix] Safely discarded an invalid object-spawn message.");
    }

    internal static void RecordPlayerStateRepair()
    {
        int count = Interlocked.Increment(ref _playerStatesRepaired);
        LogRateLimited(
            count,
            "[P2P Spawn Fix] Rebuilt missing replicated-player subelement state.");
    }

    internal static void RecordEmptyInitialStateSkipped()
    {
        int count = Interlocked.Increment(ref _emptyInitialStatesSkipped);
        LogRateLimited(
            count,
            "[P2P Spawn Fix] Skipped an impossible zero-byte initial replication state.");
    }

    internal static void RecordSensitiveInitialStatePreserved(object instance)
    {
        int count = Interlocked.Increment(ref _sensitiveInitialStatesPreserved);
        string objectName = DescribeReplicationTarget(instance);

        LogRateLimited(
            count,
            "[P2P Spawn Fix] Preserved the original initial-state behavior for " +
            "a progression-sensitive object: " + objectName + ".");
    }

    internal static void RecordDeferredPlayerResolve()
    {
        int count = Interlocked.Increment(ref _playerResolvesDeferred);
        LogRateLimited(
            count,
            "[P2P Spawn Fix] Deferred an incomplete replicated-player resolution.");
    }

    internal static void RecordInvalidPotteryGhost(
        int requestedIndex,
        int prefabCount,
        int currentIndex)
    {
        int count = Interlocked.Increment(ref _invalidPotteryGhostsBlocked);

        if (count <= 5 || count == 10 || count % 100 == 0)
        {
            Debug.LogWarning(
                "[P2P Spawn Fix] Blocked invalid PotteryTable ghost update before " +
                "SetupGhost could remove the current craft object. Requested index: " +
                requestedIndex +
                "; prefab count: " + prefabCount +
                "; current index: " + currentIndex +
                "; total blocked: " + count + ".");
        }
    }

    private static void LogRateLimited(int count, string message)
    {
        // The original fault can repeat every frame. Keep the first few events
        // visible, then report only milestones so the Unity log stays usable.
        if (count <= 3 || count == 10 || count % 100 == 0)
        {
            Debug.Log(message + " Total: " + count);
        }
    }

    internal static string GetStatus()
    {
        return
            "[P2P Spawn Fix] Active. Null arrays repaired: " +
            _nullArraysRepaired +
            "; invalid spawn messages blocked: " +
            _invalidSpawnsBlocked +
            "; empty initial states skipped: " +
            _emptyInitialStatesSkipped +
            "; sensitive initial states preserved: " +
            _sensitiveInitialStatesPreserved +
            "; player states repaired: " +
            _playerStatesRepaired +
            "; incomplete player resolves deferred: " +
            _playerResolvesDeferred +
            "; invalid pottery ghost updates blocked: " +
            _invalidPotteryGhostsBlocked +
            ".";
    }

    internal static void Reset()
    {
        _spawnDeserializeDepth = 0;
        _objectSpawnDepth = 0;
        _currentSpawnHadNullArrayRepair = false;
        Interlocked.Exchange(ref _nullArraysRepaired, 0);
        Interlocked.Exchange(ref _invalidSpawnsBlocked, 0);
        Interlocked.Exchange(ref _emptyInitialStatesSkipped, 0);
        Interlocked.Exchange(ref _sensitiveInitialStatesPreserved, 0);
        Interlocked.Exchange(ref _playerStatesRepaired, 0);
        Interlocked.Exchange(ref _playerResolvesDeferred, 0);
        Interlocked.Exchange(ref _invalidPotteryGhostsBlocked, 0);
    }

    internal static Type FindType(string typeName)
    {
        Type type = AccessTools.TypeByName(typeName);

        if (type == null)
        {
            Debug.LogWarning(
                "[P2P Spawn Fix] Game type not found: " + typeName +
                ". The related protection could not be installed.");
        }

        return type;
    }

    internal static bool IsProgressionSensitiveReplication(object instance)
    {
        if (instance == null)
        {
            return false;
        }

        Type instanceType = instance.GetType();
        if (ContainsProgressionMarker(instanceType.FullName))
        {
            return true;
        }

        Component component = instance as Component;
        if (component == null)
        {
            return false;
        }

        Transform current = component.transform;
        while (current != null)
        {
            if (ContainsProgressionMarker(current.name))
            {
                return true;
            }

            Component[] attachedComponents =
                current.gameObject.GetComponents<Component>();

            for (int i = 0; i < attachedComponents.Length; i++)
            {
                Component attached = attachedComponents[i];
                if (attached != null &&
                    ContainsProgressionMarker(attached.GetType().FullName))
                {
                    return true;
                }
            }

            current = current.parent;
        }

        return false;
    }

    private static bool ContainsProgressionMarker(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string[] markers =
        {
            "map",
            "quest",
            "notepad",
            "notebook",
            "journal",
            "blueprint",
            "recipe",
            "cartograph"
        };

        for (int i = 0; i < markers.Length; i++)
        {
            if (value.IndexOf(
                    markers[i],
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeReplicationTarget(object instance)
    {
        if (instance == null)
        {
            return "<null>";
        }

        Component component = instance as Component;
        if (component != null)
        {
            return component.gameObject.name + " (" +
                instance.GetType().Name + ")";
        }

        return instance.GetType().FullName;
    }

    internal static MethodBase FindMethod(
        string typeName,
        string methodName,
        Type returnType,
        int parameterCount)
    {
        Type type = FindType(typeName);
        if (type == null)
        {
            return null;
        }

        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic);

        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];

            if (method.Name == methodName &&
                (returnType == null || method.ReturnType == returnType) &&
                method.GetParameters().Length == parameterCount)
            {
                return method;
            }
        }

        Debug.LogWarning(
            "[P2P Spawn Fix] Game method not found: " +
            typeName + "." + methodName +
            ". The related protection could not be installed.");

        return null;
    }

    internal static bool IsTargetNullArrayException(Exception exception)
    {
        ArgumentNullException nullException = exception as ArgumentNullException;
        if (nullException == null || nullException.ParamName != "array")
        {
            return false;
        }

        string stackTrace = exception.StackTrace;
        return
            !string.IsNullOrEmpty(stackTrace) &&
            stackTrace.IndexOf(
                "P2PObjectSpawnMessage.Deserialize",
                StringComparison.Ordinal) >= 0;
    }

    internal static bool PreparePlayerSubelements(object instance)
    {
        if (instance == null)
        {
            RecordDeferredPlayerResolve();
            return false;
        }

        Type type = instance.GetType();
        FieldInfo modelField = FindInstanceField(type, "m_NetworkPlayerModel");
        FieldInfo hashField = FindInstanceField(type, "m_ReplActiveElementsHash");
        FieldInfo currentField = FindInstanceField(type, "m_ReplActiveElements");
        FieldInfo incomingField = FindInstanceField(type, "m_ReplActiveElements_Repl");

        if (modelField == null ||
            hashField == null ||
            currentField == null ||
            incomingField == null)
        {
            // Preserve the game's behavior if a future version changes its
            // internal field layout. The missing member will be visible in the
            // normal exception instead of being silently hidden.
            return true;
        }

        Transform networkModel = modelField.GetValue(instance) as Transform;
        bool[] incoming = incomingField.GetValue(instance) as bool[];

        // OnReplicationResolve cannot do useful work before both the hierarchy
        // and the newly received state exist. Skipping this one resolution is
        // safer than fabricating remote state; a later replication will retry.
        if (networkModel == null || incoming == null)
        {
            RecordDeferredPlayerResolve();
            return false;
        }

        bool repaired = false;
        int requiredLength = incoming.Length;

        bool[] current = currentField.GetValue(instance) as bool[];
        if (current == null || current.Length != requiredLength)
        {
            bool[] replacement = new bool[requiredLength];
            if (current != null)
            {
                Array.Copy(
                    current,
                    replacement,
                    Math.Min(current.Length, replacement.Length));
            }

            currentField.SetValue(instance, replacement);
            repaired = true;
        }

        int[] hashes = hashField.GetValue(instance) as int[];
        bool hashesNeedRepair =
            hashes == null ||
            hashes.Length != requiredLength ||
            !HashesMatchNetworkModel(hashes, networkModel);

        if (hashesNeedRepair)
        {
            int[] replacement = new int[requiredLength];
            int childrenToRead = Math.Min(requiredLength, networkModel.childCount);

            for (int i = 0; i < childrenToRead; i++)
            {
                Transform child = networkModel.GetChild(i);
                if (child != null)
                {
                    replacement[i] = child.name.GetHashCode();
                }
            }

            hashField.SetValue(instance, replacement);
            repaired = true;
        }

        if (repaired)
        {
            RecordPlayerStateRepair();
        }

        return true;
    }

    private static bool HashesMatchNetworkModel(
        int[] hashes,
        Transform networkModel)
    {
        if (hashes == null || networkModel == null)
        {
            return false;
        }

        for (int i = 0; i < hashes.Length; i++)
        {
            bool found = false;

            for (int childIndex = 0;
                 childIndex < networkModel.childCount;
                 childIndex++)
            {
                Transform child = networkModel.GetChild(childIndex);
                if (child != null && child.name.GetHashCode() == hashes[i])
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static FieldInfo FindInstanceField(Type type, string fieldName)
    {
        Type current = type;

        while (current != null)
        {
            FieldInfo field = current.GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            if (field != null)
            {
                return field;
            }

            current = current.BaseType;
        }

        Debug.LogWarning(
            "[P2P Spawn Fix] Game field not found: " +
            type.FullName + "." + fieldName + ".");

        return null;
    }
}

// A remote PotteryTable can replicate -1 (or another invalid value) to a
// client. Validate it before SetupGhost changes the currently displayed craft
// object and then indexes m_GhostPrefabs.
[HarmonyPatch]
internal static class PotteryTableSetupGhostGuardPatch
{
    private static FieldInfo _ghostPrefabsField;
    private static FieldInfo _currentGhostIndexField;

    private static MethodBase TargetMethod()
    {
        Type potteryType = P2PSpawnFixRuntime.FindType("PotteryTable");
        if (potteryType == null)
        {
            return null;
        }

        _ghostPrefabsField = AccessTools.Field(potteryType, "m_GhostPrefabs");
        _currentGhostIndexField =
            AccessTools.Field(potteryType, "m_CurrentGhostIndex");

        if (_ghostPrefabsField == null)
        {
            Debug.LogWarning(
                "[P2P Spawn Fix] PotteryTable.m_GhostPrefabs was not found. " +
                "The pottery protection could not be installed safely.");
            return null;
        }

        MethodBase setupGhost = P2PSpawnFixRuntime.FindMethod(
            "PotteryTable",
            "SetupGhost",
            typeof(void),
            1);

        if (setupGhost != null)
        {
            Debug.Log(
                "[P2P Spawn Fix] PotteryTable SetupGhost guard installed.");
        }

        return setupGhost;
    }

    private static bool Prefix(object __instance, int __0)
    {
        if (__instance == null || _ghostPrefabsField == null)
        {
            return true;
        }

        ICollection ghostPrefabs =
            _ghostPrefabsField.GetValue(__instance) as ICollection;

        if (ghostPrefabs == null)
        {
            return true;
        }

        int requestedIndex = __0;
        if (requestedIndex >= 0 && requestedIndex < ghostPrefabs.Count)
        {
            return true;
        }

        int currentIndex = int.MinValue;
        if (_currentGhostIndexField != null)
        {
            object currentValue = _currentGhostIndexField.GetValue(__instance);
            if (currentValue is int)
            {
                currentIndex = (int)currentValue;
            }
        }

        P2PSpawnFixRuntime.RecordInvalidPotteryGhost(
            requestedIndex,
            ghostPrefabs.Count,
            currentIndex);

        return false;
    }
}

// Opens a narrow scope only while the game is deserializing an object-spawn
// message. The reader patch below is inactive for every other network message.
[HarmonyPatch]
internal static class P2PSpawnDeserializeScopePatch
{
    private static MethodBase TargetMethod()
    {
        return P2PSpawnFixRuntime.FindMethod(
            "P2PObjectSpawnMessage",
            "Deserialize",
            null,
            1);
    }

    private static void Prefix()
    {
        P2PSpawnFixRuntime.EnterSpawnDeserialize();
    }

    private static Exception Finalizer(Exception __exception)
    {
        P2PSpawnFixRuntime.ExitSpawnDeserialize();
        return __exception;
    }
}

// Assembly-CSharp.dll confirms that ReadBytesAndSize returns null when the
// encoded size is zero. P2PObjectSpawnMessage immediately passes that value to
// ArraySegment<byte>, whose constructor rejects null. Empty arrays preserve the
// intended zero-length payload while satisfying ArraySegment's invariant.
[HarmonyPatch]
internal static class P2PSpawnByteArrayPatch
{
    private static MethodBase TargetMethod()
    {
        return P2PSpawnFixRuntime.FindMethod(
            "P2PNetworkReader",
            "ReadBytesAndSize",
            typeof(byte[]),
            0);
    }

    private static void Postfix(ref byte[] __result)
    {
        if (P2PSpawnFixRuntime.IsInsideSpawnDeserialize && __result == null)
        {
            __result = new byte[0];
            P2PSpawnFixRuntime.RecordNullArrayRepair();
        }
    }
}

// Last-resort containment. If a game update introduces another null byte-array
// path inside the same spawn deserializer, unwind the whole OnObjectSpawn call
// and discard only that malformed message. Unrelated exceptions are preserved.
[HarmonyPatch]
internal static class P2PObjectSpawnSafetyPatch
{
    private static MethodBase TargetMethod()
    {
        return P2PSpawnFixRuntime.FindMethod(
            "P2PSession",
            "OnObjectSpawn",
            null,
            1);
    }

    private static void Prefix()
    {
        P2PSpawnFixRuntime.EnterObjectSpawn();
    }

    private static Exception Finalizer(Exception __exception)
    {
        P2PSpawnFixRuntime.ExitObjectSpawn();

        if (__exception == null)
        {
            return null;
        }

        if (!P2PSpawnFixRuntime.IsTargetNullArrayException(__exception))
        {
            return __exception;
        }

        P2PSpawnFixRuntime.RecordBlockedSpawn();
        return null;
    }
}

// Replicator.OnSpawnMessage always calls
// ReplicationComponent.Deserialize(payload, true), even when the encoded spawn
// payload has zero bytes. ReplicationReceive expects a header immediately and
// its first ReadByte/ReadInt32 operation cannot succeed on that buffer. Skip
// only this impossible initial read when it belongs to the same object-spawn
// message whose null byte array was repaired. Never change unrelated initial
// reads or progression-sensitive objects such as maps and quest items.
[HarmonyPatch]
internal static class EmptyInitialReplicationStatePatch
{
    private static MethodBase TargetMethod()
    {
        return P2PSpawnFixRuntime.FindMethod(
            "ReplicationComponent",
            "Deserialize",
            typeof(void),
            2);
    }

    private static bool Prefix(object __instance, ArraySegment<byte> __0, bool __1)
    {
        ArraySegment<byte> payload = __0;
        bool initialState = __1;

        if (!initialState || payload.Count != 0)
        {
            return true;
        }

        if (!P2PSpawnFixRuntime.CanSkipCurrentEmptyInitialState)
        {
            return true;
        }

        bool progressionSensitive;

        try
        {
            progressionSensitive =
                P2PSpawnFixRuntime.IsProgressionSensitiveReplication(__instance);
        }
        catch (Exception exception)
        {
            // Object hierarchies may disappear while their network message is
            // being processed. If classification is uncertain, never suppress
            // the game's original progression or replication behavior.
            Debug.LogWarning(
                "[P2P Spawn Fix] Initial-state classification failed; " +
                "preserving the original game behavior: " + exception.Message);
            return true;
        }

        if (progressionSensitive)
        {
            P2PSpawnFixRuntime.RecordSensitiveInitialStatePreserved(__instance);
            return true;
        }

        P2PSpawnFixRuntime.RecordEmptyInitialStateSkipped();
        return false;
    }
}

// A zero-length initial spawn payload can leave the remote player's generated
// subelement array populated later while the companion hash/current-state
// arrays are still absent. The original resolver indexes all three arrays and
// dereferences the network model without guards. Rebuild only those derived
// arrays, using the same child-name hash algorithm found in the game's Awake
// method, or defer the resolution until its required state exists.
[HarmonyPatch]
internal static class ReplicatedPlayerSubelementsResolvePatch
{
    private static MethodBase TargetMethod()
    {
        return P2PSpawnFixRuntime.FindMethod(
            "ReplicatedPlayerSubelements",
            "OnReplicationResolve",
            typeof(void),
            0);
    }

    private static bool Prefix(object __instance)
    {
        try
        {
            return P2PSpawnFixRuntime.PreparePlayerSubelements(__instance);
        }
        catch (Exception exception)
        {
            P2PSpawnFixRuntime.RecordDeferredPlayerResolve();
            Debug.LogWarning(
                "[P2P Spawn Fix] Player subelement repair was deferred: " +
                exception.Message);
            return false;
        }
    }

    private static Exception Finalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
        {
            P2PSpawnFixRuntime.RecordDeferredPlayerResolve();
            return null;
        }

        return __exception;
    }
}
