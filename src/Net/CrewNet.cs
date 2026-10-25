using System;
using HarmonyLib;
using MedusaMultiCrew.Crew;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace MedusaMultiCrew.Net
{
    // Owns the link between the mod and Mirage: finds the network manager, registers our
    // handlers each time the game creates a new server or client MessageHandler (it makes a
    // fresh one on every start), and sends the one-time hello. Nothing but CrewHelloMsg is
    // ever sent to a peer before it has proven it runs the mod, because unknown messages
    // cost the receiving peer an error penalty (MessageHandler.InvokeHandler -> SetError(60)).
    internal static class CrewNet
    {
        // Bump Protocol on any wire change. CrewHelloMsg / CrewHelloAckMsg layouts must never change.
        public const byte Protocol = 2;
        public const ushort Version = 2;

        private static NetworkManagerNuclearOption manager;
        private static float nextManagerSearch;

        private static MessageHandler serverHandler;
        private static MessageHandler clientHandler;
        private static NetworkServer disconnectHookedServer;
        private static bool helloSent;

        public static NetworkServer Server => serverHandler != null ? manager.Server : null;
        public static NetworkClient Client => clientHandler != null ? manager.Client : null;

        // Client side: the server confirmed it runs a matching version of the mod.
        public static bool ServerHasMod { get; private set; }

        // Raised on the aircraft owner for each validated frame from a seated WSO.
        public static event Action<CrewControlFrameMsg> ControlFrameReceived;

        public static void Tick()
        {
            if (!FindManager())
            {
                return;
            }

            NetworkServer server = manager.Server;
            if (server != null && server.Active && server.MessageHandler != null)
            {
                if (!ReferenceEquals(server.MessageHandler, serverHandler))
                {
                    RegisterServer(server);
                }
                CrewServer.Tick();
            }
            else if (serverHandler != null)
            {
                serverHandler = null;
                CrewServer.Reset();
                Plugin.Log.LogInfo("[Net] Server stopped; crew state cleared");
            }

            NetworkClient client = manager.Client;
            if (client != null && client.Active && client.MessageHandler != null)
            {
                if (!ReferenceEquals(client.MessageHandler, clientHandler))
                {
                    RegisterClient(client);
                }
                TrySendHello(client);
            }
            else if (clientHandler != null)
            {
                ResetClient();
                Plugin.Log.LogInfo("[Net] Client stopped; crew state cleared");
            }
        }

        public static void ResetAll()
        {
            serverHandler = null;
            CrewServer.Reset();
            ResetClient();
        }

        private static void ResetClient()
        {
            clientHandler = null;
            helloSent = false;
            ServerHasMod = false;
            CrewClient.Reset();
        }

        // ---- client -> server ----

        public static void SendToServer<T>(T msg)
        {
            // Only after the server proved it has the mod; hello goes through TrySendHello.
            if (!ServerHasMod || clientHandler == null)
            {
                return;
            }
            try
            {
                manager.Client.Send(msg);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Send {typeof(T).Name} failed: {e.Message}");
            }
        }

        // ---- server -> client ----

        public static void SendToPlayer<T>(INetworkPlayer player, T msg)
        {
            if (player == null)
            {
                return;
            }
            try
            {
                player.Send(msg);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Send {typeof(T).Name} to {player} failed: {e.Message}");
            }
        }

        internal static void SetServerHasMod(bool value) => ServerHasMod = value;

        internal static void RaiseControlFrame(CrewControlFrameMsg msg)
        {
            try
            {
                ControlFrameReceived?.Invoke(msg);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Control frame handler failed: {e}");
            }
        }

        private static bool FindManager()
        {
            if (manager != null)
            {
                return true;
            }
            if (Time.unscaledTime < nextManagerSearch)
            {
                return false;
            }
            nextManagerSearch = Time.unscaledTime + 2f;
            manager = UnityEngine.Object.FindObjectOfType<NetworkManagerNuclearOption>();
            return manager != null;
        }

        private static void RegisterServer(NetworkServer server)
        {
            CrewSerializers.Register();
            CrewServer.Reset();
            serverHandler = server.MessageHandler;

            serverHandler.RegisterHandler<CrewHelloMsg>(Guard<CrewHelloMsg>(CrewServer.OnHello), false);
            serverHandler.RegisterHandler<CrewJoinRequestMsg>(Guard<CrewJoinRequestMsg>(CrewServer.OnJoinRequest), false);
            serverHandler.RegisterHandler<CrewJoinDecisionMsg>(Guard<CrewJoinDecisionMsg>(CrewServer.OnJoinDecision), false);
            serverHandler.RegisterHandler<CrewLeaveMsg>(Guard<CrewLeaveMsg>(CrewServer.OnLeave), false);
            serverHandler.RegisterHandler<CrewControlFrameMsg>(Guard<CrewControlFrameMsg>(CrewServer.OnControlFrame), false);

            // The server object lives across sessions, so hook its disconnect event only once.
            if (!ReferenceEquals(disconnectHookedServer, server))
            {
                disconnectHookedServer = server;
                server.Disconnected.AddListener(OnServerDisconnected);
            }
            Plugin.Log.LogInfo("[Net] Server handlers registered");
        }

        private static void RegisterClient(NetworkClient client)
        {
            CrewSerializers.Register();
            ResetClient();
            clientHandler = client.MessageHandler;

            clientHandler.RegisterHandler<CrewHelloAckMsg>(Guard<CrewHelloAckMsg>(CrewClient.OnHelloAck), false);
            clientHandler.RegisterHandler<CrewJoinPromptMsg>(Guard<CrewJoinPromptMsg>(CrewClient.OnJoinPrompt), false);
            clientHandler.RegisterHandler<CrewJoinResultMsg>(Guard<CrewJoinResultMsg>(CrewClient.OnJoinResult), false);
            clientHandler.RegisterHandler<CrewRosterMsg>(Guard<CrewRosterMsg>(CrewClient.OnRoster), false);
            clientHandler.RegisterHandler<CrewControlFrameMsg>(Guard<CrewControlFrameMsg>(CrewClient.OnControlFrame), false);
            Plugin.Log.LogInfo("[Net] Client handlers registered");
        }

        private static void TrySendHello(NetworkClient client)
        {
            // Having a local Player object means we're authenticated and in a mission.
            if (helloSent || !GameManager.GetLocalPlayer<Player>(out _))
            {
                return;
            }
            helloSent = true;
            try
            {
                client.Send(new CrewHelloMsg { Protocol = Protocol, Version = Version });
                Plugin.Log.LogInfo("[Net] Sent hello to server");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Hello failed: {e.Message}");
            }
        }

        private static void OnServerDisconnected(INetworkPlayer player)
        {
            try
            {
                if (serverHandler != null)
                {
                    CrewServer.OnDisconnected(player);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Disconnect cleanup failed: {e}");
            }
        }

        // Exceptions escaping a handler make Mirage charge the sender error points (and the
        // server rate-limits errors), so every handler is wrapped.
        private static MessageDelegateWithPlayer<T> Guard<T>(Action<INetworkPlayer, T> handler)
        {
            return (player, msg) =>
            {
                try
                {
                    handler(player, msg);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[Net] {typeof(T).Name} handler failed: {e}");
                }
            };
        }

        [HarmonyPatch(typeof(NetworkManagerNuclearOption), nameof(NetworkManagerNuclearOption.Awake))]
        private static class NetworkManagerAwakePatch
        {
            private static void Postfix(NetworkManagerNuclearOption __instance) => manager = __instance;
        }
    }
}
