#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OpenRA.FileFormats;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA.Widgets.Delegates
{
	public class LobbyDelegate : IWidgetDelegate
	{
		Widget LocalPlayerTemplate, RemotePlayerTemplate, EmptySlotTemplate, EmptySlotTemplateHost;
		ScrollPanelWidget Players;
		Dictionary<string, string> CountryNames;
		string MapUid;
		Map Map;

        public static ColorRamp CurrentColorPreview;

		readonly OrderManager orderManager;
		[ObjectCreator.UseCtor]
		internal LobbyDelegate( [ObjectCreator.Param( "widget" )] Widget lobby, [ObjectCreator.Param] OrderManager orderManager )
		{
			this.orderManager = orderManager;
			Game.LobbyInfoChanged += UpdateCurrentMap;
			UpdateCurrentMap();
			
			CurrentColorPreview = Game.Settings.Player.ColorRamp;

			Players = lobby.GetWidget<ScrollPanelWidget>("PLAYERS");
			LocalPlayerTemplate = Players.GetWidget("TEMPLATE_LOCAL");
			RemotePlayerTemplate = Players.GetWidget("TEMPLATE_REMOTE");
			EmptySlotTemplate = Players.GetWidget("TEMPLATE_EMPTY");
			EmptySlotTemplateHost = Players.GetWidget("TEMPLATE_EMPTY_HOST");

			var mapPreview = lobby.GetWidget<MapPreviewWidget>("LOBBY_MAP_PREVIEW");
			mapPreview.Map = () => Map;
			mapPreview.OnMouseDown = mi =>
			{
				var map = mapPreview.Map();
				if (map == null || mi.Button != MouseButton.Left
				    || orderManager.LocalClient.State == Session.ClientState.Ready)
					return false;

				var p = map.Waypoints
					.Select((sp, i) => Pair.New(mapPreview.ConvertToPreview(map, sp.Value), i))
					.Where(a => (a.First - mi.Location).LengthSquared < 64)
					.Select(a => a.Second + 1)
					.FirstOrDefault();

				var owned = orderManager.LobbyInfo.Clients.Any(c => c.SpawnPoint == p);
				if (p == 0 || !owned)
					orderManager.IssueOrder(Order.Command("spawn {0}".F(p)));
				
				return true;
			};

			mapPreview.SpawnColors = () =>
			{
				var spawns = Map.SpawnPoints;
				var sc = new Dictionary<int2, Color>();

				for (int i = 1; i <= spawns.Count(); i++)
				{
					var client = orderManager.LobbyInfo.Clients.FirstOrDefault(c => c.SpawnPoint == i);
					if (client == null)
						continue;
					sc.Add(spawns.ElementAt(i - 1), client.ColorRamp.GetColor(0));
				}
				return sc;
			};

			CountryNames = Rules.Info["world"].Traits.WithInterface<OpenRA.Traits.CountryInfo>().ToDictionary(a => a.Race, a => a.Name);
			CountryNames.Add("random", "Random");

			var mapButton = lobby.GetWidget("CHANGEMAP_BUTTON");
			mapButton.OnMouseUp = mi =>
			{
				Widget.OpenWindow( "MAP_CHOOSER", new Dictionary<string, object> { { "orderManager", orderManager }, { "mapName", MapUid } } );
				return true;
			};

			mapButton.IsVisible = () => mapButton.Visible && Game.IsHost;

			var disconnectButton = lobby.GetWidget("DISCONNECT_BUTTON");
			disconnectButton.OnMouseUp = mi =>
			{
				Game.Disconnect();
				return true;
			};
			
			var lockTeamsCheckbox = lobby.GetWidget<CheckboxWidget>("LOCKTEAMS_CHECKBOX");
			lockTeamsCheckbox.BindReadOnly(orderManager.LobbyInfo.GlobalSettings, "LockTeams");
			lockTeamsCheckbox.OnChange += _ =>
			{
				if (Game.IsHost)
					orderManager.IssueOrder(Order.Command(
						"lockteams {0}".F(!orderManager.LobbyInfo.GlobalSettings.LockTeams)));
			};
			
			var startGameButton = lobby.GetWidget("START_GAME_BUTTON");
			startGameButton.OnMouseUp = mi =>
			{
				mapButton.Visible = false;
				disconnectButton.Visible = false;
				lockTeamsCheckbox.Visible = false;
				orderManager.IssueOrder(Order.Command("startgame"));
				return true;
			};
			
			// Todo: Only show if the map requirements are met for player slots
			startGameButton.IsVisible = () => Game.IsHost;
			Game.LobbyInfoChanged += UpdatePlayerList;
			
			Game.AddChatLine += lobby.GetWidget<ChatDisplayWidget>("CHAT_DISPLAY").AddLine;

			bool teamChat = false;
			var chatLabel = lobby.GetWidget<LabelWidget>("LABEL_CHATTYPE");
			var chatTextField = lobby.GetWidget<TextFieldWidget>("CHAT_TEXTFIELD");
			chatTextField.OnEnterKey = () =>
			{
				if (chatTextField.Text.Length == 0)
					return true;

				var order = (teamChat) ? Order.TeamChat(chatTextField.Text) : Order.Chat(chatTextField.Text);
				orderManager.IssueOrder(order);
				chatTextField.Text = "";
				return true;
			};

			chatTextField.OnTabKey = () =>
			{
				teamChat ^= true;
				chatLabel.Text = (teamChat) ? "Team:" : "Chat:";
				return true;
			};
		}
		
		void UpdatePlayerColor(float hf, float sf, float lf, float r)
		{
			var ramp = new ColorRamp((byte) (hf*255), (byte) (sf*255), (byte) (lf*255), (byte)(r*255));
			Game.Settings.Player.ColorRamp = ramp;
			Game.Settings.Save();
			orderManager.IssueOrder(Order.Command("color {0}".F(ramp)));
		}
		
		void UpdateColorPreview(float hf, float sf, float lf, float r)
		{
            CurrentColorPreview = new ColorRamp((byte)(hf * 255), (byte)(sf * 255), (byte)(lf * 255), (byte)(r * 255));
		}

		void UpdateCurrentMap()
		{
			if (MapUid == orderManager.LobbyInfo.GlobalSettings.Map) return;
			MapUid = orderManager.LobbyInfo.GlobalSettings.Map;
			Map = new Map(Game.modData.AvailableMaps[MapUid].Path);

			var title = Widget.RootWidget.GetWidget<LabelWidget>("LOBBY_TITLE");
			title.Text = "OpenRA Multiplayer Lobby - " + orderManager.LobbyInfo.GlobalSettings.ServerName;
		}

		Session.Client GetClientInSlot(Session.Slot slot)
		{
			return orderManager.LobbyInfo.ClientInSlot( slot );
		}

		bool ShowSlotDropDown(Session.Slot slot, ButtonWidget name, bool showBotOptions)
		{
			var dropDownOptions = new List<Pair<string, Action>>
			{
				new Pair<string, Action>( "Open",
					() => orderManager.IssueOrder( Order.Command( "slot_open " + slot.Index )  )),
				new Pair<string, Action>( "Closed",
					() => orderManager.IssueOrder( Order.Command( "slot_close " + slot.Index ) )),
			};

			if (showBotOptions)
			{
				var bots = Rules.Info["player"].Traits.WithInterface<IBotInfo>().Select(t => t.Name);
				bots.Do(bot =>
					dropDownOptions.Add(new Pair<string, Action>("Bot: {0}".F(bot),
						() => orderManager.IssueOrder(Order.Command("slot_bot {0} {1}".F(slot.Index, bot))))));
			}

			DropDownButtonWidget.ShowDropDown( name,
				dropDownOptions,
				(ac, w) => new LabelWidget
				{
					Bounds = new Rectangle(0, 0, w, 24),
					Text = "  {0}".F(ac.First),
					OnMouseUp = mi => { ac.Second(); return true; },
				});
			return true;
		}
		
		bool ShowRaceDropDown(Session.Slot s, ButtonWidget race)
		{
			if (Map.Players[s.MapPlayer].LockRace)
				return false;

			var dropDownOptions = new List<Pair<string, Action>>();
			foreach (var c in CountryNames)
			{
				var cc = c;
				dropDownOptions.Add(new Pair<string, Action>( cc.Key,
					() => orderManager.IssueOrder( Order.Command("race "+cc.Key) )) );
			};

			DropDownButtonWidget.ShowDropDown( race,
				dropDownOptions,
				(ac, w) =>
			    {
					var ret = new LabelWidget
					{
						Bounds = new Rectangle(0, 0, w, 24),
						Text = "          {0}".F(CountryNames[ac.First]),
						OnMouseUp = mi => { ac.Second(); return true; },
					};
				
					ret.AddChild(new ImageWidget
					{
						Bounds = new Rectangle(5, 5, 40, 15),
						GetImageName = () => ac.First,
						GetImageCollection = () => "flags",
					});
					return ret;
				});
			return true;
		}
		
		bool ShowTeamDropDown(ButtonWidget team)
		{
			var dropDownOptions = new List<Pair<string, Action>>();
			for (int i = 0; i <= Map.PlayerCount; i++)
			{
				var ii = i;
				dropDownOptions.Add(new Pair<string, Action>( ii == 0 ? "-" : ii.ToString(),
					() => orderManager.IssueOrder( Order.Command("team "+ii) )) );
			};

			DropDownButtonWidget.ShowDropDown( team,
				dropDownOptions,
				(ac, w) => new LabelWidget
				{
					Bounds = new Rectangle(0, 0, w, 24),
					Text = "  {0}".F(ac.First),
					OnMouseUp = mi => { ac.Second(); return true; },
				});
			return true;
		}
		
		bool ShowColorDropDown(Session.Slot s, ButtonWidget color)
		{
			if (Map.Players[s.MapPlayer].LockColor)
				return false;
			
			var colorChooser = Game.modData.WidgetLoader.LoadWidget( new Dictionary<string,object>(), null, "COLOR_CHOOSER" );
			var hueSlider = colorChooser.GetWidget<SliderWidget>("HUE_SLIDER");
			hueSlider.SetOffset(orderManager.LocalClient.ColorRamp.H / 255f);
			
			var satSlider = colorChooser.GetWidget<SliderWidget>("SAT_SLIDER");
            satSlider.SetOffset(orderManager.LocalClient.ColorRamp.S / 255f);

			var lumSlider = colorChooser.GetWidget<SliderWidget>("LUM_SLIDER");
            lumSlider.SetOffset(orderManager.LocalClient.ColorRamp.L / 255f);
			
			var rangeSlider = colorChooser.GetWidget<SliderWidget>("RANGE_SLIDER");
            rangeSlider.SetOffset(orderManager.LocalClient.ColorRamp.R / 255f);
			
			hueSlider.OnChange += _ => UpdateColorPreview(hueSlider.GetOffset(), satSlider.GetOffset(), lumSlider.GetOffset(), rangeSlider.GetOffset());
			satSlider.OnChange += _ => UpdateColorPreview(hueSlider.GetOffset(), satSlider.GetOffset(), lumSlider.GetOffset(), rangeSlider.GetOffset());
			lumSlider.OnChange += _ => UpdateColorPreview(hueSlider.GetOffset(), satSlider.GetOffset(), lumSlider.GetOffset(), rangeSlider.GetOffset());
			rangeSlider.OnChange += _ => UpdateColorPreview(hueSlider.GetOffset(), satSlider.GetOffset(), lumSlider.GetOffset(), rangeSlider.GetOffset());
			UpdateColorPreview(hueSlider.GetOffset(), satSlider.GetOffset(), lumSlider.GetOffset(), rangeSlider.GetOffset());

			DropDownButtonWidget.ShowDropPanel(color, colorChooser, new List<Widget>() {colorChooser.GetWidget("BUTTON_OK")}, () => {
				UpdateColorPreview(hueSlider.GetOffset(), satSlider.GetOffset(), lumSlider.GetOffset(), rangeSlider.GetOffset());
				UpdatePlayerColor(hueSlider.GetOffset(), satSlider.GetOffset(), lumSlider.GetOffset(), rangeSlider.GetOffset());
				return true;
			});
			return true;
		}
		
		void UpdatePlayerList()
		{
			// This causes problems for people who are in the process of editing their names (the widgets vanish from beneath them)
			// Todo: handle this nicer
			Players.ClearChildren();
			
			foreach (var slot in orderManager.LobbyInfo.Slots)
			{
				var s = slot;
				var c = GetClientInSlot(s);
				Widget template;

				if (c == null)
				{
					if (Game.IsHost)
					{
						if (slot.Spectator)
						{
							template = EmptySlotTemplateHost.Clone();
							var name = template.GetWidget<ButtonWidget>("NAME");
							name.GetText = () => s.Closed ? "Closed" : "Open";
							name.OnMouseDown = _ => ShowSlotDropDown(s, name, false);
							var btn = template.GetWidget<ButtonWidget>("JOIN");
							btn.GetText = () =>  "Spectate in this slot";							
						}
						else
						{
							template = EmptySlotTemplateHost.Clone();
							var name = template.GetWidget<ButtonWidget>("NAME");
							name.GetText = () => s.Closed ? "Closed" : (s.Bot == null) ? "Open" : s.Bot;
							name.OnMouseDown = _ => ShowSlotDropDown(s, name, Map.Players[ s.MapPlayer ].AllowBots);
						}
					}
					else
					{
						template = EmptySlotTemplate.Clone();
						var name = template.GetWidget<LabelWidget>("NAME");
						name.GetText = () => s.Closed ? "Closed" : (s.Bot == null) ? "Open" : s.Bot;

						if (slot.Spectator)
						{
							var btn = template.GetWidget<ButtonWidget>("JOIN");
							btn.GetText = () => "Spectate in this slot";
						}
					}

					var join = template.GetWidget<ButtonWidget>("JOIN");
					if (join != null)
					{
						join.OnMouseUp = _ => { orderManager.IssueOrder(Order.Command("slot " + s.Index)); return true; };
						join.IsVisible = () => !s.Closed && s.Bot == null && orderManager.LocalClient.State != Session.ClientState.Ready;
					}
					
					var bot = template.GetWidget<LabelWidget>("BOT");
					if (bot != null)
						bot.IsVisible = () => s.Bot != null;
				}
				else if (c.Index == orderManager.LocalClient.Index && c.State != Session.ClientState.Ready)
				{
					template = LocalPlayerTemplate.Clone();
					var name = template.GetWidget<TextFieldWidget>("NAME");
					name.Text = c.Name;
					name.OnEnterKey = () =>
					{
						name.Text = name.Text.Trim();
						if (name.Text.Length == 0)
							name.Text = c.Name;

						name.LoseFocus();
						if (name.Text == c.Name)
							return true;

						orderManager.IssueOrder(Order.Command("name " + name.Text));
						Game.Settings.Player.Name = name.Text;
						Game.Settings.Save();
						return true;
					};
					name.OnLoseFocus = () => name.OnEnterKey();

					var color = template.GetWidget<ButtonWidget>("COLOR");
					color.OnMouseUp = _ => ShowColorDropDown(s, color);
					
					var colorBlock = color.GetWidget<ColorBlockWidget>("COLORBLOCK");
					colorBlock.GetColor = () => c.ColorRamp.GetColor(0);

					var faction = template.GetWidget<ButtonWidget>("FACTION");
					faction.OnMouseDown = _ => ShowRaceDropDown(s, faction);
					
					var factionname = faction.GetWidget<LabelWidget>("FACTIONNAME");
					factionname.GetText = () => CountryNames[c.Country];
					var factionflag = faction.GetWidget<ImageWidget>("FACTIONFLAG");
					factionflag.GetImageName = () => c.Country;
					factionflag.GetImageCollection = () => "flags";

					var team = template.GetWidget<ButtonWidget>("TEAM");
					team.OnMouseDown = _ => ShowTeamDropDown(team);
					team.GetText = () => (c.Team == 0) ? "-" : c.Team.ToString();

					var status = template.GetWidget<CheckboxWidget>("STATUS");
					status.IsChecked = () => c.State == Session.ClientState.Ready;
					status.OnChange += CycleReady;
					
					var spectator = template.GetWidget<LabelWidget>("SPECTATOR");
					
					Session.Slot slot1 = slot;
					color.IsVisible = () => !slot1.Spectator;
					colorBlock.IsVisible = () => !slot1.Spectator;
					faction.IsVisible = () => !slot1.Spectator;
					factionname.IsVisible = () => !slot1.Spectator;
					factionflag.IsVisible = () => !slot1.Spectator;
					team.IsVisible = () => !slot1.Spectator;
					spectator.IsVisible = () => slot1.Spectator || slot1.Bot != null;
				}
				else
				{
					template = RemotePlayerTemplate.Clone();
					template.GetWidget<LabelWidget>("NAME").GetText = () => c.Name;
					var color = template.GetWidget<ColorBlockWidget>("COLOR");
					color.GetColor = () => c.ColorRamp.GetColor(0);

					var faction = template.GetWidget<LabelWidget>("FACTION");
					var factionname = faction.GetWidget<LabelWidget>("FACTIONNAME");
					factionname.GetText = () => CountryNames[c.Country];
					var factionflag = faction.GetWidget<ImageWidget>("FACTIONFLAG");
					factionflag.GetImageName = () => c.Country;
					factionflag.GetImageCollection = () => "flags";

					var team = template.GetWidget<LabelWidget>("TEAM");
					team.GetText = () => (c.Team == 0) ? "-" : c.Team.ToString();

					var status = template.GetWidget<CheckboxWidget>("STATUS");
					status.IsChecked = () => c.State == Session.ClientState.Ready;
					if (c.Index == orderManager.LocalClient.Index)
						status.OnChange += CycleReady;

					var spectator = template.GetWidget<LabelWidget>("SPECTATOR");
							
					Session.Slot slot1 = slot;
					color.IsVisible = () => !slot1.Spectator;
					faction.IsVisible = () => !slot1.Spectator;
					factionname.IsVisible = () => !slot1.Spectator;
					factionflag.IsVisible = () => !slot1.Spectator;
					team.IsVisible = () => !slot1.Spectator;
					spectator.IsVisible = () => slot1.Spectator || slot1.Bot != null;

					var kickButton = template.GetWidget<ButtonWidget>("KICK");
					kickButton.IsVisible = () => Game.IsHost && c.Index != orderManager.LocalClient.Index;
					kickButton.OnMouseUp = mi =>
						{
							orderManager.IssueOrder(Order.Command("kick " + c.Slot));
							return true;
						};
				}

				template.Id = "SLOT_{0}".F(s.Index);
				template.IsVisible = () => true;
				Players.AddChild(template);
			}				
		}

		bool SpawnPointAvailable(int index) { return (index == 0) || orderManager.LobbyInfo.Clients.All(c => c.SpawnPoint != index); }

		void CycleReady(bool ready)
		{
			orderManager.IssueOrder(Order.Command("ready"));
		}
	}
}
