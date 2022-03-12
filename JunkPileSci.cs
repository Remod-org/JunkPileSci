#region License (GPL v3)
/*
    DESCRIPTION
    Copyright (c) 2022 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v3)
using Oxide.Core;
using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("JunkPileSci", "RFC1920", "1.0.4")]
    [Description("A stopgap for early 2022 to add junkpile scientists back into the game")]
    internal class JunkPileSci : RustPlugin
    {
        private ConfigData configData;
        public static JunkPileSci Instance;

        private bool pluginReady;
        private Dictionary<ulong, ulong> scijunk = new Dictionary<ulong, ulong>();
        private Dictionary<ulong, Vector3> scipos = new Dictionary<ulong, Vector3>();
        private const string sci = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_junkpile_pistol.prefab";
        private const string permUse = "junkpilesci.use";

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        private void LMessage(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        #region commands
        [Command("jps")]
        private void cmdJPS(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.IsAdmin) return;
            BasePlayer bp = iplayer.Object as BasePlayer;

            if (configData.debug)
            {
                string debug = string.Join(",", args);
                Puts($"{debug}");
            }
            if (args.Length == 1 && args[0] == "tp")
            {
                float minDist = Mathf.Infinity;
                Vector3 target = Vector3.zero;
                foreach (Vector3 loc in scipos.Values)
                {
                    float dist = Vector3.Distance(loc, bp.transform.position);
                    if (dist < minDist)
                    {
                        target = loc;
                        minDist = dist;
                    }
                }
                if (target != Vector3.zero)
                {
                    Teleport(bp, target);
                }
                return;
            }

            string jplist = "";
            foreach (KeyValuePair<ulong, Vector3> jps in scipos)
            {
                jplist += $"JPS {jps.Key.ToString()} at {jps.Value.ToString()}\n";
            }
            Message(iplayer, jplist);
        }
        #endregion

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You don't have permission to do that !!"
            }, this);
        }

        private void OnServerInitialized()
        {
            AddCovalenceCommand("jps", "cmdJPS");
            permission.RegisterPermission(permUse, this);
            LoadConfigVariables();

            Instance = this;
            pluginReady = true;
        }

        public void Teleport(BasePlayer player, Vector3 position)
        {
            if (player.net?.connection != null)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
            }

            player.SetParent(null, true, true);
            player.EnsureDismounted();
            player.Teleport(position);
            player.UpdateNetworkGroup();
            player.StartSleeping();
            player.SendNetworkUpdateImmediate(false);

            if (player.net?.connection != null)
            {
                player.ClientRPCPlayer(null, player, "StartLoading");
            }
        }

        private void DoLog(string message)
        {
            if (configData.debug) Interface.Oxide.LogInfo(message);
        }

        private void Unload()
        {
            foreach (KeyValuePair<ulong, ulong> jps in scijunk)
            {
                BaseNetworkable.serverEntities.Find((uint)jps.Value)?.Kill();
            }
            DestroyAll<JPViableCheck>();
        }

        private void DestroyAll<T>() where T : MonoBehaviour
        {
            foreach (T type in UnityEngine.Object.FindObjectsOfType<T>())
            {
                UnityEngine.Object.Destroy(type);
            }
        }

        private void SpawnBot(Vector3 pos, ulong pileid)
        {
            if (pos == default(Vector3)) return;
            if (BadLocation(pos))
            {
                DoLog("Possible floating junkpile.  Skipping...");
                return;
            }
            foreach (KeyValuePair<ulong, Vector3> sci in scipos.Where(x => Vector3.Distance(pos, x.Value) < configData.minDistance))
            {
                DoLog("Too close to existing junkpile scientist.  Skipping...");
                return;
            }
            DoLog($"Spawning junkpile scientist at {pos.ToString()}");
            global::HumanNPC bot = (global::HumanNPC)GameManager.server.CreateEntity(sci, pos, new Quaternion(), true);
            bot.Spawn();

            if (bot.InSafeZone() && ! configData.allowSafeZone)
            {
                DoLog("Junkpile scientist spawned in safe zone.  Removing...");
                bot?.Kill();
                return;
            }
            bot.displayName = "JPS";

            NextTick(() =>
            {
                JPViableCheck jpr = bot.gameObject.AddComponent<JPViableCheck>();
                jpr.pileid = pileid;
                bot.startHealth = configData.defaultHealth;
                bot.Brain.Navigator.Agent.agentTypeID = -1372625422;
                bot.Brain.Navigator.DefaultArea = "Walkable";
                bot.Brain.Navigator.Init(bot, bot.Brain.Navigator.Agent);
                bot.Brain.ForceSetAge(0);
                bot.Brain.TargetLostRange = configData.targetRange;
                bot.Brain.HostileTargetsOnly = !configData.hostile;
                bot.Brain.Navigator.BestCoverPointMaxDistance = 5;
                bot.Brain.Navigator.BestRoamPointMaxDistance = configData.roamRange;
                bot.Brain.Navigator.MaxRoamDistanceFromHome = configData.roamRange;
                bot.Brain.Senses.Init(bot, configData.botMemory, configData.roamRange, configData.targetRange, -1f, true, false, true, configData.listenRange, true, false, true, EntityType.Player, false);

                if (!bot.Brain.Navigator.Agent.isOnNavMesh)
                {
                    DoLog("JPS not spawned on navmesh.  Removing...");
                    bot?.Kill();
                    return;
                }
            });

            scijunk.Add(pileid, bot.net.ID);
            scipos.Add(bot.net.ID, bot.transform.position);
            DoLog($"Current JPS count is {scipos.Count.ToString()}");
        }

        private void OnEntitySpawned(JunkPile pile)
        {
            if (!pluginReady) return;
            if (UnityEngine.Random.Range(1, 101) < configData.spawnPercentage)
            {
                SpawnBot(pile.transform.position, pile.net.ID);
            }
        }

        private void OnEntityKill(JunkPile pile)
        {
            if (pile == null) return;
            ulong botid = scijunk.ContainsKey(pile.net.ID) ? scijunk[pile.net.ID] : 0;
            if (botid > 0)
            {
                DoLog($"Killing jps {botid.ToString()} associated with junkpile {pile.net.ID.ToString()}");
                BaseNetworkable.serverEntities.Find((uint)botid)?.Kill();
                scijunk.Remove(pile.net.ID);
                scipos.Remove(botid);
            }
        }

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Minimum spawn distance between scientists")]
            public float minDistance;

            [JsonProperty(PropertyName = "Default scientist health")]
            public float defaultHealth;

            [JsonProperty(PropertyName = "Hostile")]
            public bool hostile;

            [JsonProperty(PropertyName = "Allow in safe zone")]
            public bool allowSafeZone;

            [JsonProperty(PropertyName = "Roam range")]
            public float roamRange;

            [JsonProperty(PropertyName = "Target lost range")]
            public float targetRange;

            [JsonProperty(PropertyName = "Listen range")]
            public float listenRange;

            [JsonProperty(PropertyName = "Memory duration")]
            public float botMemory;

            [JsonProperty(PropertyName = "Spawn percentage vs junk pile spawns")]
            public int spawnPercentage;

            public bool debug;
            public VersionNumber Version;
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData()
            {
                minDistance = 75f,
                defaultHealth = 120f,
                allowSafeZone = false,
                hostile = false,
                roamRange = 10f,
                targetRange = 75f,
                listenRange = 30f,
                botMemory = 30f,
                spawnPercentage = 50,
                debug = false
            };

            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private bool BadLocation(Vector3 location)
        {
            int layerMask = LayerMask.GetMask("Water");
            RaycastHit hit;
            return Physics.Raycast(new Ray(location, Vector3.down), out hit, 6f, layerMask);
        }

        public class JPViableCheck : FacepunchBehaviour
        {
            public ulong pileid;
            private JunkPile junkpile;
            private global::HumanNPC bot;

            public void Awake()
            {
                bot = GetComponent<global::HumanNPC>();
            }

            public void FixedUpdate()
            {
                if (bot == null) return;
                if (pileid > 0 && junkpile == null)
                {
                    junkpile = BaseNetworkable.serverEntities.Find((uint)pileid) as JunkPile;
                }
                if (junkpile == null) return;

                if (Vector3.Distance(bot.transform.position, junkpile.transform.position) > 50f || junkpile.IsDestroyed)
                {
                    Instance.scijunk.Remove(pileid);
                    Instance.scipos.Remove(bot.net.ID);
                    Instance.DoLog($"Junkpile out of range or destroyed.  Killing JPS at {bot?.transform.position.ToString()}.  Current count is {Instance.scipos.Count.ToString()}.");
                    bot?.Kill();
                    Destroy(this);
                }
            }
        }
    }
}
