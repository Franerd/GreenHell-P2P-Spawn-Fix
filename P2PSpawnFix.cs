using System;
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

    private static int _nullArraysRepaired;
    private static int _invalidSpawnsBlocked;
    private static int _emptyInitialStatesSkipped;
    private static int _playerStatesRepaired;
    private static int _playerResolvesDeferred;

    internal static bool IsInsideSpawnDeserialize
    {
        get { return _spawnDeserializeDepth > 0; }
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

    internal static void RecordDeferredPlayerResolve()
    {
        int count = Interlocked.Increment(ref _playerResolvesDeferred);
        LogRateLimited(
            count,
            "[P2P Spawn Fix] Deferred an incomplete replicated-player resolution.");
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
            "; player states repaired: " +
            _playerStatesRepaired +
            "; incomplete player resolves deferred: " +
            _playerResolvesDeferred +
            ".";
    }

    internal static void Reset()
    {
        _spawnDeserializeDepth = 0;
        Interlocked.Exchange(ref _nullArraysRepaired, 0);
        Interlocked.Exchange(ref _invalidSpawnsBlocked, 0);
        Interlocked.Exchange(ref _emptyInitialStatesSkipped, 0);
        Interlocked.Exchange(ref _playerStatesRepaired, 0);
        Interlocked.Exchange(ref _playerResolvesDeferred, 0);
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

    private static Exception Finalizer(Exception __exception)
    {
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
// only this impossible initial read; the object-spawn method can then finish
// registering the object and later replication messages can provide state.
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

    private static bool Prefix(ArraySegment<byte> __0, bool __1)
    {
        ArraySegment<byte> payload = __0;
        bool initialState = __1;

        if (!initialState || payload.Count != 0)
        {
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
