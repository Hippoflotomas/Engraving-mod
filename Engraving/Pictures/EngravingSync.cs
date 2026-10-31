using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Engraving.Pictures
{
    /// <summary>
    /// Pictures between players, through the server (modelled on Heraldry Mod's ChargeSync; Engraving's own).
    /// <list type="bullet">
    /// <item>Joining: the server sends the list of its EngravingsServer pictures (fingerprints, names, sizes).
    /// They're fetched without asking whenever a carving uses one.</item>
    /// <item>Other players' pictures (a carving made with someone's own Engravings file) are fetched only once
    /// approved with engrave_get. The server answers from its folder or its relay memory, or asks the players
    /// online; the first copy that checks out goes to everyone waiting and stays in memory (never on disk).</item>
    /// </list>
    /// Every picture is checked against its fingerprint and the size limits by whoever receives it. When this
    /// computer is the server (hosting, single player) its own requests are handled in-process.
    /// The mod has to be on the server for pictures to be shared.
    /// </summary>
    internal static class EngravingSync
    {
        const string RpcName = "Engraving_Pictures";
        const int ProtocolVersion = 1;
        const long Local = long.MinValue;   // "this computer" as the requester, when it's also the server

        // client -> server
        const byte Hello = 1, Want = 2, Offer = 3;
        // server -> client
        const byte Catalogue = 10, PictureMsg = 11, Unavailable = 12, Ask = 13;

        enum State { None, Server, Remote }

        static CustomRPC _rpc;
        static State _state;

        // client
        static readonly Dictionary<string, float> Requested = new Dictionary<string, float>();
        static long _received, _uploaded;
        static float _lastReminder = -999f;
        static readonly HashSet<string> Encountered = new HashSet<string>();

        // server
        static readonly Dictionary<string, HashSet<long>> Waiters = new Dictionary<string, HashSet<long>>();
        static readonly Dictionary<string, float> Asked = new Dictionary<string, float>();
        static readonly Dictionary<string, (string name, byte[] bytes)> Relay = new Dictionary<string, (string, byte[])>();
        static readonly LinkedList<string> RelayOrder = new LinkedList<string>();
        static long _relayBytes;
        static readonly Dictionary<long, long> UploadsByPeer = new Dictionary<long, long>();

        public static void Init()
        {
            _rpc = NetworkManager.Instance.AddRPC(RpcName, OnServerReceive, OnClientReceive);
        }

        /// <summary>From the plugin's Update: notices starting, joining and leaving games.</summary>
        public static void Update()
        {
            var znet = ZNet.instance;
            if (znet == null)
            {
                if (_state != State.None) Reset();
                return;
            }
            if (znet.IsServer())
            {
                if (_state == State.Server) return;
                _state = State.Server;
                int n = EngravingCharges.LoadServerFolder();
                Jotunn.Logger.LogInfo($"[Engraving] Serving {n} picture(s) from {EngravingCharges.ServerFolder}.");
                return;
            }
            var peer = znet.GetServerPeer();
            if (_state == State.None && peer != null && peer.IsReady())
            {
                _state = State.Remote;
                _received = _uploaded = 0;
                var pkg = new ZPackage();
                pkg.Write(Hello);
                pkg.Write(ProtocolVersion);
                _rpc.SendPackage(peer.m_uid, pkg);
            }
        }

        static void Reset()
        {
            _state = State.None;
            Requested.Clear();
            Encountered.Clear();
            Waiters.Clear();
            Asked.Clear();
            Relay.Clear();
            RelayOrder.Clear();
            _relayBytes = 0;
            UploadsByPeer.Clear();
            EngravingCharges.LeaveServer();
        }

        static void Tell(string text)
        {
            global::Console.instance?.Print("[Engraving] " + text);
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, text);
        }

        // ================================================================== client: asking

        static bool IsRequested(string fp) => Requested.TryGetValue(fp, out float t) && Time.time - t < 60f;

        /// <summary>Asks for these pictures. Returns how many were asked for.</summary>
        public static int Request(IEnumerable<string> fingerprints)
        {
            var list = new List<string>();
            foreach (var fp in fingerprints)
            {
                if (EngravingCharges.Bytes(fp) != null || IsRequested(fp)) continue;
                if (_received > EngravingPlugin.MaxDownloadBytes)
                {
                    Tell($"Not fetched: you've downloaded MaxPictureDownloadMB this session.");
                    break;
                }
                Requested[fp] = Time.time;
                EngravingCharges.SetStatus(fp, "fetching");
                list.Add(fp);
            }
            if (list.Count == 0) return 0;
            if (_state == State.Server) ServerWant(Local, list);
            else if (_state == State.Remote)
            {
                var pkg = new ZPackage();
                pkg.Write(Want);
                pkg.Write(list.Count);
                foreach (var fp in list) pkg.Write(fp);
                _rpc.SendPackage(ZNet.instance.GetServerPeer().m_uid, pkg);
            }
            else
            {
                foreach (var fp in list) { Requested.Remove(fp); EngravingCharges.SetStatus(fp, "not in a game"); }
                return 0;
            }
            return list.Count;
        }

        /// <summary>The reminder when carvings show pictures you haven't got (several gathered into one message).</summary>
        public static void RemindEncounter(string name)
        {
            Encountered.Add(name ?? "?");
            if (Time.time - _lastReminder < 8f) return;
            _lastReminder = Time.time;
            string names = string.Join(", ", Encountered.Take(4)) + (Encountered.Count > 4 ? ", ..." : "");
            Encountered.Clear();
            Tell($"Carvings here use pictures you haven't got ({names}). Type 'engrave_pending' in the console (F5) to see them, 'engrave_get all' to fetch them.");
        }

        // ================================================================== client: receiving

        static IEnumerator OnClientReceive(long sender, ZPackage pkg)
        {
            try
            {
                switch (pkg.ReadByte())
                {
                    case Catalogue: ReadCatalogue(pkg); break;
                    case PictureMsg: ReceivePicture(pkg.ReadString(), pkg.ReadString(), pkg.ReadByteArray()); break;
                    case Unavailable:
                    {
                        string fp = pkg.ReadString(), why = pkg.ReadString();
                        Requested.Remove(fp);
                        EngravingCharges.SetStatus(fp, why);
                        Tell($"Picture '{EngravingCharges.NameOf(fp) ?? fp}': {why}.");
                        break;
                    }
                    case Ask: AnswerAsk(pkg); break;
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[Engraving] Bad picture message from the server: {e.Message}");
            }
            yield break;
        }

        static void ReadCatalogue(ZPackage pkg)
        {
            int n = Math.Min(pkg.ReadInt(), 5000);
            var list = new List<(string, string, long)>();
            for (int i = 0; i < n; i++)
            {
                string fp = pkg.ReadString(), name = pkg.ReadString();
                long size = pkg.ReadLong();
                if (EngravingCharges.IsFingerprint(fp)) list.Add((fp, name, size));
            }
            EngravingCharges.SetCatalogue(list);
            Jotunn.Logger.LogInfo($"[Engraving] The server offers {list.Count} picture(s).");
        }

        static void ReceivePicture(string fp, string name, byte[] bytes)
        {
            fp = fp.ToLowerInvariant();
            if (!Requested.Remove(fp))
            {
                Jotunn.Logger.LogWarning("[Engraving] Ignored a picture that wasn't asked for.");
                return;
            }
            _received += bytes.Length;
            string problem = EngravingCharges.Problem(fp, bytes, EngravingPlugin.MaxPictureBytes);
            if (problem != null)
            {
                EngravingCharges.SetStatus(fp, "refused: " + problem);
                Tell($"A picture was refused: {problem}.");
                return;
            }
            EngravingCharges.AddDownloaded(fp, name, bytes);
            Tell($"Got the picture '{name}'.");
        }

        /// <summary>The server asks who has these pictures: send the ones this computer has (if sharing is on).</summary>
        static void AnswerAsk(ZPackage pkg)
        {
            int n = Math.Min(pkg.ReadInt(), 64);
            for (int i = 0; i < n; i++)
            {
                string fp = pkg.ReadString();
                if (!EngravingPlugin.SharePictures.Value) continue;
                var bytes = EngravingCharges.Bytes(fp);
                if (bytes == null || bytes.Length > EngravingPlugin.MaxPictureBytes) continue;
                if (_uploaded + bytes.Length > EngravingPlugin.MaxUploadBytes) continue;
                _uploaded += bytes.Length;
                var reply = new ZPackage();
                reply.Write(Offer);
                reply.Write(fp);
                reply.Write(EngravingCharges.NameOf(fp) ?? "picture");
                reply.Write(bytes);
                _rpc.SendPackage(ZNet.instance.GetServerPeer().m_uid, reply);
            }
        }

        // ================================================================== server

        static IEnumerator OnServerReceive(long sender, ZPackage pkg)
        {
            try
            {
                switch (pkg.ReadByte())
                {
                    case Hello:
                        if (pkg.ReadInt() == ProtocolVersion) Welcome(sender);
                        break;
                    case Want:
                    {
                        int n = Math.Min(pkg.ReadInt(), 256);
                        var list = new List<string>();
                        for (int i = 0; i < n; i++) list.Add(pkg.ReadString().ToLowerInvariant());
                        ServerWant(sender, list);
                        break;
                    }
                    case Offer: ServerOffer(sender, pkg.ReadString().ToLowerInvariant(), pkg.ReadString(), pkg.ReadByteArray()); break;
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[Engraving] Bad picture message from a player: {e.Message}");
            }
            yield break;
        }

        static void Welcome(long peer)
        {
            var served = EngravingCharges.ServerPictures().Where(e => e.Size <= EngravingPlugin.MaxPictureBytes).ToList();
            var pkg = new ZPackage();
            pkg.Write(Catalogue);
            pkg.Write(served.Count);
            foreach (var e in served)
            {
                pkg.Write(e.Fingerprint);
                pkg.Write(e.Name ?? "");
                pkg.Write(e.Size);
            }
            _rpc.SendPackage(peer, pkg);

            // someone new might have what others are waiting for
            var wanted = Waiters.Keys.ToList();
            if (wanted.Count > 0) SendAsk(peer, wanted);
        }

        static void ServerWant(long peer, List<string> fingerprints)
        {
            var toAsk = new List<string>();
            foreach (var fp in fingerprints)
            {
                if (!EngravingCharges.IsFingerprint(fp)) continue;
                var own = EngravingCharges.IsFromServer(fp) ? EngravingCharges.Bytes(fp) : null;
                if (own != null) { SendPicture(peer, fp, EngravingCharges.NameOf(fp), own); continue; }
                if (Relay.TryGetValue(fp, out var r)) { SendPicture(peer, fp, r.name, r.bytes); continue; }
                if (!Waiters.TryGetValue(fp, out var set)) Waiters[fp] = set = new HashSet<long>();
                set.Add(peer);
                if (!Asked.TryGetValue(fp, out float t) || Time.time - t > 20f)
                {
                    Asked[fp] = Time.time;
                    toAsk.Add(fp);
                }
            }
            if (toAsk.Count == 0) return;
            // the host's own pictures count too
            foreach (var fp in toAsk.ToList())
            {
                var mine = EngravingPlugin.SharePictures.Value ? EngravingCharges.Bytes(fp) : null;
                if (mine == null) continue;
                Deliver(fp, EngravingCharges.NameOf(fp), mine);
                toAsk.Remove(fp);
            }
            if (toAsk.Count == 0) return;
            var peers = ZNet.instance.GetPeers().Where(p => p.m_uid != peer).ToList();
            foreach (var p in peers) SendAsk(p.m_uid, toAsk);
            if (peers.Count == 0)
                foreach (var fp in toAsk) NobodyHasIt(fp);
            EngravingPlugin.Instance.StartCoroutine(GiveUp(toAsk, 20f));
        }

        static IEnumerator GiveUp(List<string> fingerprints, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            foreach (var fp in fingerprints)
                if (Waiters.ContainsKey(fp)) NobodyHasIt(fp);
        }

        static void NobodyHasIt(string fp)
        {
            if (!Waiters.TryGetValue(fp, out var set)) return;
            Waiters.Remove(fp);
            foreach (var peer in set) SendUnavailable(peer, fp, "nobody online has it right now; try 'engrave_get' again later");
        }

        static void ServerOffer(long peer, string fp, string name, byte[] bytes)
        {
            if (!Waiters.ContainsKey(fp)) return;   // not wanted (any more)
            UploadsByPeer.TryGetValue(peer, out long up);
            if (up + bytes.Length > EngravingPlugin.MaxUploadBytes * 4) return;
            UploadsByPeer[peer] = up + bytes.Length;
            if (EngravingCharges.Problem(fp, bytes, EngravingPlugin.MaxPictureBytes) != null) return;
            Deliver(fp, name, bytes);
        }

        /// <summary>A checked picture: to everyone waiting, and into relay memory.</summary>
        static void Deliver(string fp, string name, byte[] bytes)
        {
            Remember(fp, name, bytes);
            if (!Waiters.TryGetValue(fp, out var set)) return;
            Waiters.Remove(fp);
            foreach (var w in set) SendPicture(w, fp, name, bytes);
        }

        /// <summary>Relay memory: recently passed-on pictures, up to RelayCacheMB, oldest dropped first. Never on disk.</summary>
        static void Remember(string fp, string name, byte[] bytes)
        {
            if (Relay.ContainsKey(fp)) return;
            long max = EngravingPlugin.RelayCacheBytes;
            if (bytes.Length > max) return;
            while (_relayBytes + bytes.Length > max && RelayOrder.Count > 0)
            {
                var old = RelayOrder.First.Value;
                RelayOrder.RemoveFirst();
                _relayBytes -= Relay[old].bytes.Length;
                Relay.Remove(old);
            }
            Relay[fp] = (name, bytes);
            RelayOrder.AddLast(fp);
            _relayBytes += bytes.Length;
        }

        static void SendPicture(long peer, string fp, string name, byte[] bytes)
        {
            if (peer == Local)
            {
                if (!Requested.Remove(fp)) return;
                if (EngravingCharges.Bytes(fp) == null) EngravingCharges.AddDownloaded(fp, name, bytes);
                return;
            }
            var pkg = new ZPackage();
            pkg.Write(PictureMsg);
            pkg.Write(fp);
            pkg.Write(name ?? "picture");
            pkg.Write(bytes);
            _rpc.SendPackage(peer, pkg);
        }

        static void SendUnavailable(long peer, string fp, string why)
        {
            if (peer == Local)
            {
                Requested.Remove(fp);
                EngravingCharges.SetStatus(fp, why);
                Tell($"Picture '{EngravingCharges.NameOf(fp) ?? fp}': {why}.");
                return;
            }
            var pkg = new ZPackage();
            pkg.Write(Unavailable);
            pkg.Write(fp);
            pkg.Write(why);
            _rpc.SendPackage(peer, pkg);
        }

        static void SendAsk(long peer, List<string> fingerprints)
        {
            var pkg = new ZPackage();
            pkg.Write(Ask);
            pkg.Write(fingerprints.Count);
            foreach (var fp in fingerprints) pkg.Write(fp);
            _rpc.SendPackage(peer, pkg);
        }
    }
}
