using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
#if UNITY_EDITOR
using Unity.Multiplayer.Playmode;
#endif

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance;

    [Tooltip("Set to 'production' unless you made a test environment")]
    public string environmentName = "production";

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }
    }

    public async Task InitializeServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions();
            options.SetEnvironmentName(string.IsNullOrWhiteSpace(environmentName) ? "production" : environmentName);
#if UNITY_EDITOR
            // Multiplayer Play Mode instances must use different UGS profiles so
            // they receive different anonymous Authentication player IDs.
            options.SetProfile(CurrentPlayer.IsMainEditor ? "main-editor" : "virtual-player");
#endif
            await UnityServices.InitializeAsync(options);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[UGS] Signed in anonymously as {AuthenticationService.Instance.PlayerId}");
        }
    }

    void ResetNetworking()
    {
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
    }

    void OnEnable()
    {
        if (NetworkManager.Singleton == null) return;

        var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        utp.OnTransportEvent += HandleTransportEvent;

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
    }

    void OnDisable()
    {
        if (NetworkManager.Singleton == null) return;
        var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        utp.OnTransportEvent -= HandleTransportEvent;

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
    }

    void HandleClientConnected(ulong id) => Debug.Log($"[Netcode] Client connected: {id}");
    void HandleClientDisconnected(ulong id) => Debug.Log($"[Netcode] Client disconnected: {id}");
    void HandleServerStarted() => Debug.Log("[Netcode] Server/Host started");

    void HandleTransportEvent(NetworkEvent evt, ulong _, System.ArraySegment<byte> __, float ___)
    {
        if (evt == NetworkEvent.TransportFailure)
        {
            Debug.LogWarning("[Transport] Failure detected. Tearing down.");
            NetworkManager.Singleton.Shutdown();
            // UI tip: disable Join/Host buttons until a brand-new allocation is created
        }
    }


    static string GetConnectionType()
    {
        // WSS works in both the Editor and WebGL and avoids networks that block
        // the UDP traffic used by DTLS. It also makes local testing match the web build.
        return "wss";
    }

    UnityTransport ConfigureRelayTransport(Allocation allocation)
    {
        var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        string connectionType = GetConnectionType();
        utp.UseWebSockets = connectionType == "wss";
        utp.SetRelayServerData(allocation.ToRelayServerData(connectionType));
        Debug.Log($"[Relay] Using {connectionType.ToUpperInvariant()}");
        return utp;
    }

    UnityTransport ConfigureRelayTransport(JoinAllocation allocation)
    {
        var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        string connectionType = GetConnectionType();
        utp.UseWebSockets = connectionType == "wss";
        utp.SetRelayServerData(allocation.ToRelayServerData(connectionType));
        Debug.Log($"[Relay] Using {connectionType.ToUpperInvariant()}");
        return utp;
    }

    public async Task<string> CreateRelayAndStartHostAsync(int maxConnections = 32)
    {
        try
        {
            ResetNetworking();
            await InitializeServicesAsync();

            var alloc = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            Debug.Log($"[Relay] Join Code: {joinCode}");

            ConfigureRelayTransport(alloc);

            if (!NetworkManager.Singleton.StartHost())
            {
                Debug.LogError("[Relay] StartHost() failed");
                return "";
            }

            return joinCode;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay][Host] {e}");
            throw;
        }
    }

    // Dedicated/headless builds use a server with no local player.
    public async Task<string> CreateRelayAndStartServerAsync(int maxConnections = 32)
    {
        try
        {
            ResetNetworking();
            await InitializeServicesAsync();

            var alloc = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            Debug.Log($"[Relay] Join Code: {joinCode}");

            ConfigureRelayTransport(alloc);

            if (!NetworkManager.Singleton.StartServer())
            {
                Debug.LogError("[Relay] StartServer() failed");
                return "";
            }

            return joinCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay][Server] {e}");
            throw;
        }
    }

    public async Task JoinRelayAndStartClientAsync(string joinCode)
    {
        TaskCompletionSource<bool> connectionResult = null;
        Action<ulong> connectedHandler = null;
        Action<ulong> disconnectedHandler = null;

        try
        {
            ResetNetworking();
            await InitializeServicesAsync();

            var joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim().ToUpperInvariant());
            ConfigureRelayTransport(joinAlloc);

            connectionResult = new TaskCompletionSource<bool>();
            connectedHandler = id =>
            {
                if (id == NetworkManager.Singleton.LocalClientId)
                {
                    connectionResult.TrySetResult(true);
                }
            };
            disconnectedHandler = id =>
            {
                if (!NetworkManager.Singleton.IsConnectedClient)
                {
                    connectionResult.TrySetResult(false);
                }
            };

            NetworkManager.Singleton.OnClientConnectedCallback += connectedHandler;
            NetworkManager.Singleton.OnClientDisconnectCallback += disconnectedHandler;

            if (!NetworkManager.Singleton.StartClient())
            {
                throw new InvalidOperationException("StartClient() failed");
            }

            Task finishedTask = await Task.WhenAny(connectionResult.Task, Task.Delay(15000));
            if (finishedTask != connectionResult.Task)
            {
                NetworkManager.Singleton.Shutdown();
                throw new TimeoutException("The Relay connection timed out after 15 seconds.");
            }

            if (!await connectionResult.Task)
            {
                throw new InvalidOperationException("The Relay server disconnected before the client connected.");
            }

            Debug.Log("[Relay] Client connection confirmed");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay][Client] {e}");
            throw;
        }
        finally
        {
            if (NetworkManager.Singleton != null)
            {
                if (connectedHandler != null)
                    NetworkManager.Singleton.OnClientConnectedCallback -= connectedHandler;
                if (disconnectedHandler != null)
                    NetworkManager.Singleton.OnClientDisconnectCallback -= disconnectedHandler;
            }
        }
    }
}
