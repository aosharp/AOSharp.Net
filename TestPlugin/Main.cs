using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Core.Inventory;
using AOSharp.Core.Movement;
using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Core.GameData;
using AOSharp.Core.UI.Options;
using AOSharp.Core.IPC;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.SharedEventArgs;
using AOSharp.Common.Helpers;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core.GMI;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;
using SmokeLounge.AOtomation.Messaging.GameData;
using TestPlugin.IPCMessages;

namespace TestPlugin
{
    public class Main : AOPluginEntry
    {
        public Settings? Settings;
        private IPCChannel? _ipcChannel;
        private MovementController? mc;
        private Window? testWindow;
        private Menu? _menu;

        [DllImport("DisplaySystem.dll", EntryPoint = "?ClearAnimations@VisualCATMesh_t@@QAEXXZ", CallingConvention = CallingConvention.ThisCall)]
        public static extern void ClearAnimations(IntPtr pVisualCatmesh);

        public override void Run()
        {
            Chat.WriteLine("TestPlugin loaded");

            Chat.WriteLine($"LocalPlayer: {DynelManager.LocalPlayer.Identity}");
            Chat.WriteLine($"   Name: {DynelManager.LocalPlayer.Name}");
            Chat.WriteLine($"   Pos: {DynelManager.LocalPlayer.Position}");
            Chat.WriteLine($"   MoveState: {DynelManager.LocalPlayer.MovementState}");
            Chat.WriteLine($"   Health: {DynelManager.LocalPlayer.GetStat(Stat.Health)}");

            Chat.WriteLine("Team:");
            Chat.WriteLine($"\tIsInTeam: {Team.IsInTeam}");
            Chat.WriteLine($"\tIsLeader: {Team.IsLeader}");
            Chat.WriteLine($"\tIsRaid: {Team.IsRaid}");

            Chat.WriteLine($"Camera:");
            Chat.WriteLine($"IsFirstPerson: {Camera.IsFirstPerson}");
            Chat.WriteLine($"Position: {Camera.Position}");
            Chat.WriteLine($"Rot: {Camera.Rotation}");

            mc = new MovementController(drawPath: true);

            foreach (TeamMember teamMember in Team.Members)
                Chat.WriteLine($"\t{teamMember.Name} - {teamMember.Identity} - {teamMember.Level} - {teamMember.Profession} - IsLeader:{teamMember.IsLeader} @ Team {teamMember.TeamIndex + 1}");

            Chat.WriteLine("Dynels:");
            foreach (SimpleChar c in DynelManager.Characters)
                Chat.WriteLine($"\t{c.Name}\t{((int)c.Flags).ToString("X4")}\t{c.Profession}");

            foreach (PerkAction perkAction in PerkAction.List)
                Chat.WriteLine($"{perkAction.Name} = 0x{((uint)perkAction.Hash).ToString("X4")},");

            Chat.WriteLine($"Missions ({Mission.List.Count})");
            foreach (Mission mission in Mission.List)
            {
                Chat.WriteLine($"   {mission.Identity}");
                Chat.WriteLine($"       Ptr: {mission.Pointer.ToString("X4")}");
                Chat.WriteLine($"       Source: {mission.Source}");
                Chat.WriteLine($"       Playfield: {(mission.Location != null ? mission.Location.Playfield.ToString() : "NULL")}");
                Chat.WriteLine($"       WorldPos: {(mission.Location != null ? mission.Location.Pos.ToString() : "NULL")}");
                Chat.WriteLine($"       DisplayName: {mission.DisplayName}");
            }

            //_menu = new Menu("TestPlugin", "TestPlugin");
            //_menu.AddItem(new MenuBool("DrawingTest", "Drawing Test", false));
            //OptionPanel.AddMenu(_menu);

            Chat.RegisterCommand("modlist", (string command, string[] param, ChatWindow chatWindow) =>
            {
                if (DummyItem.CreateDummyItemID(302724, 302724, 300, out Identity item))
                {
                    N3EngineClientAnarchy.DebugSpellListToChat(item, 1, 0xE);
                    Chat.WriteLine(Utils.FindPattern("Gamecode.dll", "55 8B EC 8B 41 24 8B 4D 08 8B 04 88 5D C2 04 00").ToString("X"));
                }
            });

            Chat.RegisterCommand("kdtree", (string command, string[] param, ChatWindow chatWindow) =>
            {
                VisualEnvFX_t.ToggleRandyDebuggerKDTreeDisplay(VisualEnvFX_t.GetInstance());
            });

            Chat.RegisterCommand("modlistspell", (string command, string[] param, ChatWindow chatWindow) =>
            {
                if (Spell.Find("Mongo Bash!", out Spell item))
                {
                    Chat.WriteLine("SpellList:");
                    foreach (SpellData spell in item.UseModifiers)
                    {
                        Chat.WriteLine($"\t{spell.Function} - {spell.GetType()}");
                        foreach (var prop in spell.Properties)
                            Chat.WriteLine($"\t\t{prop.Key}: {prop.Value}");
                    }
                }
            });

            Chat.RegisterCommand("healing", (string command, string[] param, ChatWindow chatWindow) =>
            {
                if (Inventory.Find("Health and Nano Stim", out Item item))
                {
                    int targetHealing = item.UseModifiers.Where(x => x is SpellData.Healing hx && hx.ApplyOn == SpellModifierTarget.Target).Cast<SpellData.Healing>().Sum(x => x.Average);
                    Chat.WriteLine($"{item.Name} @ {item.QualityLevel} heals targets for {targetHealing}");
                }
            });

            Chat.RegisterCommand("dumpops", (string command, string[] param, ChatWindow chatWindow) =>
            {
                for (int i = 0; i < 300; i++)
                {
                    string str = DevExtras.SpellOperatorToString(i);
                    if (str == string.Empty || str.Contains("Missing SpellData"))
                        continue;
                    Chat.WriteLine($"{str} = {i},");
                }
            });

            Chat.RegisterCommand("dueltarget", (string command, string[] param, ChatWindow chatWindow) =>
            {
                if (Targeting.TargetChar != null)
                    Duel.Challenge(Targeting.Target.Identity);
            });

            Chat.RegisterCommand("split", (string command, string[] param, ChatWindow chatWindow) =>
            {
                if (param.Length >= 2 && Inventory.Find(int.Parse(param[0]), out Item item))
                    item.Split(int.Parse(param[1]));
            });

            Chat.RegisterCommand("testui", (string command, string[] param, ChatWindow chatWindow) =>
            {
                Chat.WriteLine($"{ItemListViewBase_c.Create(new Rect(999999, 999999, -999999, -999999), 0, 0, 0, Identity.None).ToString("X4")}");
            });

            Chat.RegisterCommand("dumppf", (string command, string[] param, ChatWindow chatWindow) =>
            {
                for (int i = 100; i < ushort.MaxValue; i++)
                {
                    IntPtr pName = N3InterfaceModule_t.GetPFName(i);
                    if (pName == IntPtr.Zero)
                        continue;
                    Chat.WriteLine($"{Utils.UnsafePointerToString(pName).Replace(" ", "").Replace("'", "")} = {i},");
                }
            });

            Chat.RegisterCommand("testbsjoin", (string command, string[] param, ChatWindow chatWindow) =>
            {
                Battlestation.JoinQueue(Battlestation.Side.Blue);
            });

            Chat.RegisterCommand("testbsleave", (string command, string[] param, ChatWindow chatWindow) =>
            {
                Battlestation.LeaveQueue();
            });

            Chat.RegisterCommand("openwindow", (string command, string[] param, ChatWindow chatWindow) =>
            {
                testWindow = Window.CreateFromXml("Test", $"{PluginDirectory}\\TestWindow.xml");
                testWindow.Show(true);
                chatWindow.WriteLine($"Window.Pointer: {testWindow.Pointer.ToString("X4")}");
                chatWindow.WriteLine($"Window.Name: {testWindow.Name}");

                if (!testWindow.IsValid)
                    return;

                if (testWindow.FindView("testTextView", out TextView testView))
                {
                    testView.SetDefaultColor(0xFF00E8);
                    testView.Text = "1337";
                }

                if (testWindow.FindView("testPowerBar", out PowerBarView testPowerBar))
                    testPowerBar.Value = 0.1f;

                if (testWindow.FindView("testButton", out Button testButton))
                {
                    testButton.Clicked += OnTestButtonClicked;
                    testButton.SetLabelColor(0xFF0000);
                    testButton.SetGfx(ButtonState.Pressed, "GFX_GUI_BS_REDSTAR");
                }

                if (testWindow.FindView("testButton2", out ButtonBase testButton2))
                    testButton2.Clicked += OnTestButtonClicked;

                if (testWindow.FindView("testComboBox", out ComboBox testComboBox))
                {
                    for (int i = 0; i < 10; i++)
                        testComboBox.AppendItem(i, $"loli {i}");
                }

                if (testWindow.FindView("testTextInput", out TextInputView testTextInput))
                {
                    testTextInput.SetAlpha(0.7f);
                    testTextInput.Text = "head pats";
                }

                if (testWindow.FindView("testBitmapView", out BitmapView testBitmapView))
                    testBitmapView.SetBitmap("GFX_GUI_SHORT_HOR_BAR_EMPTY");
            });

            Chat.RegisterCommand("savesettings", (string command, string[] param, ChatWindow chatWindow) =>
            {
                Settings.Save();
            });

            try
            {
                IPCChannel testChannel1 = new IPCChannel(225);
                IPCChannel testChannel2 = new IPCChannel(226);
                IPCChannel testChannel3 = new IPCChannel(227);
                IPCChannel testChannel4 = new IPCChannel(228);
                IPCChannel testChannel5 = new IPCChannel(229);

                _ipcChannel = new IPCChannel(1);

                _ipcChannel.RegisterCallback((int)IPCOpcode.Test, (sender, msg) =>
                {
                    TestMessage testMsg = (TestMessage)msg;
                    Chat.WriteLine($"TestMessage: {testMsg.Leet} - {testMsg.Position}");
                });

                _ipcChannel.RegisterCallback((int)IPCOpcode.Empty, (sender, msg) =>
                {
                    Chat.WriteLine("EmptyMessage");
                });
            }
            catch (Exception e)
            {
                Chat.WriteLine(e.Message);
            }

            Settings = new Settings("TestPlugin");
            Settings.AddVariable("DrawStuff", false);
            Settings.AddVariable("AnotherVariable", 1911);

            Game.OnUpdate += OnUpdate;
            Game.TeleportStarted += Game_OnTeleportStarted;
            Game.TeleportEnded += Game_OnTeleportEnded;
            Game.TeleportFailed += Game_OnTeleportFailed;
            Game.PlayfieldInit += Game_PlayfieldInit;
            Network.N3MessageReceived += Network_N3MessageReceived;
            Network.PacketReceived += Network_PacketReceived;
            Network.ChatMessageReceived += Network_ChatMessageReceived;
            Team.TeamRequest += Team_TeamRequest;
            Team.MemberLeft += Team_MemberLeft;
            Item.ItemUsed += Item_ItemUsed;
            NpcDialog.AnswerListChanged += NpcDialog_AnswerListChanged;
            Inventory.ContainerOpened += OnContainerOpened;
            Duel.Challenged = DuelChallenged;
            Duel.StatusChanged = DuelStatusChanged;
            CharacterAction.Inspect += OnInspect;
        }

        private void OnInspect(object sender, InspectEventArgs inspectArgs)
        {
            foreach (var page in inspectArgs.Pages)
            {
                Chat.WriteLine(page.Key);
                foreach (var slot in page.Value)
                    Chat.WriteLine($"{slot.EquipSlot} {slot.LowId} {slot.HighId} {slot.Ql}");
            }
        }

        private void DuelChallenged(object sender, DuelRequestEventArgs e)
        {
            Chat.WriteLine($"{e.Challenger} challenged us to a duel!");
            e.Accept();
        }

        private void DuelStatusChanged(object sender, DuelStatusChangedEventArgs e)
        {
            Chat.WriteLine($"Duel Status changed to {e.Status} ({e.Opponent})");
        }

        private void OnTestButtonClicked(object s, ButtonBase button)
        {
            Chat.WriteLine($"TestButton Clicked! {button.Pointer.ToString("X4")}");
        }

        private void OnContainerOpened(object sender, Container container)
        {
            Chat.WriteLine($"Container {container.Identity} opened.");
            foreach (Item item in container.Items)
                Chat.WriteLine($"\t{item.Name} @ {item.Slot}");
        }

        private void Network_ChatMessageReceived(object s, SmokeLounge.AOtomation.Messaging.Messages.ChatMessageBody chatMessage)
        {
            if (chatMessage.PacketType == ChatMessageType.PrivateMessage)
                Chat.WriteLine($"Received {((PrivateMsgMessage)chatMessage).Text}");
        }

        private void Network_N3MessageReceived(object s, SmokeLounge.AOtomation.Messaging.Messages.N3Message n3Msg)
        {
            if (n3Msg.N3MessageType == N3MessageType.Trade)
            {
                TradeMessage ayy = (TradeMessage)n3Msg;
                Chat.WriteLine($"Trade: TradeAction: {ayy.Action}\tParam1: {ayy.Param1}\tParam2: {ayy.Param2}\tParam3: {ayy.Param3}\tParam4: {ayy.Param4}\t{ayy.Unknown1}");
            }

            if (n3Msg.N3MessageType == N3MessageType.SimpleCharFullUpdate)
            {
                SimpleCharFullUpdateMessage scfu = (SimpleCharFullUpdateMessage)n3Msg;
                if (scfu.Flags2 == ScfuFlags2.HasOwner)
                    Chat.WriteLine($"{scfu.Name} has flag 4. Owner: {scfu.Owner}");
            }
        }

        private void Team_TeamRequest(object s, TeamRequestEventArgs e)
        {
            e.Accept();
        }

        private void Team_MemberLeft(object s, Identity leaver)
        {
            Chat.WriteLine($"Player {leaver} left the team.");
        }

        private void NpcDialog_AnswerListChanged(object s, Dictionary<int, string> options)
        {
        }

        private void Game_OnTeleportStarted(object s, EventArgs e)
        {
            Chat.WriteLine("Teleport Started!");
        }

        private void Game_PlayfieldInit(object s, uint id)
        {
            Chat.WriteLine($"PlayfieldInit: {id}");
        }

        private void Game_OnTeleportFailed(object s, EventArgs e)
        {
            Chat.WriteLine("Teleport Failed!");
        }

        private void Game_OnTeleportEnded(object s, EventArgs e)
        {
            Chat.WriteLine("Teleport Ended!");
        }

        private void Item_ItemUsed(object s, ItemUsedEventArgs e)
        {
            Chat.WriteLine($"Item {e.Item.Name} used by {e.OwnerIdentity}");
        }

        private void OnUpdate(object s, float deltaTime)
        {
            if (testWindow != null && testWindow.IsValid)
            {
                if (testWindow.FindView("testTextView", out TextView testView))
                    testView.Text = Targeting.TargetChar == null ? "<No Target>" : Targeting.TargetChar.Position.ToString();

                if (testWindow.FindView("testPowerBar", out PowerBarView testPowerBar))
                {
                    if (Targeting.TargetChar == null)
                        testPowerBar.Value = 0;
                    else
                        testPowerBar.Value = Math.Min(1f, DynelManager.LocalPlayer.GetLogicalRangeToTarget(Targeting.TargetChar) / 20f);
                }
            }

            if (Settings["DrawStuff"].AsBool())
            {
                foreach (Dynel player in DynelManager.Players)
                {
                    Debug.DrawSphere(player.Position, 1, DebuggingColor.LightBlue);
                    Debug.DrawLine(DynelManager.LocalPlayer.Position, player.Position, DebuggingColor.LightBlue);
                }
            }

            if (DynelManager.LocalPlayer.FightingTarget != null)
            {
                if (SpecialAttack.FastAttack.IsInRange(DynelManager.LocalPlayer.FightingTarget) && SpecialAttack.FastAttack.IsAvailable())
                    SpecialAttack.FastAttack.UseOn(DynelManager.LocalPlayer.FightingTarget);
            }

            foreach (SimpleChar character in DynelManager.Characters)
            {
                if (character.IsPathing)
                {
                    Debug.DrawLine(character.Position, character.PathingDestination, DebuggingColor.LightBlue);
                    Debug.DrawSphere(character.PathingDestination, 0.2f, DebuggingColor.LightBlue);
                }
            }

            Vector3 rayOrigin = DynelManager.LocalPlayer.Position;
            Vector3 rayTarget = DynelManager.LocalPlayer.Position;
            rayTarget.Y = 0;

            if (Playfield.Raycast(rayOrigin, rayTarget, out Vector3 hitPos, out Vector3 hitNormal))
            {
                Debug.DrawLine(rayOrigin, rayTarget, DebuggingColor.White);
                Debug.DrawLine(hitPos, hitPos + hitNormal, DebuggingColor.Yellow);
                Debug.DrawSphere(hitPos, 0.2f, DebuggingColor.White);
                Debug.DrawSphere(hitPos + hitNormal, 0.2f, DebuggingColor.Yellow);
            }
        }

        private void Network_PacketReceived(object s, byte[] packet)
        {
            N3MessageType msgType = (N3MessageType)((packet[16] << 24) + (packet[17] << 16) + (packet[18] << 8) + packet[19]);

            if (msgType == N3MessageType.SimpleCharFullUpdate)
                File.WriteAllBytes($"SCFU/{(int)((packet[24] << 24) + (packet[25] << 16) + (packet[26] << 8) + packet[27])}", packet);
        }

        public override void Teardown()
        {
            Chat.WriteLine("Teardown time!");
        }
    }
}
