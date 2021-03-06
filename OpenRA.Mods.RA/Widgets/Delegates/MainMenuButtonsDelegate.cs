#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using System.Collections.Generic;
using OpenRA.FileFormats;
using OpenRA.Network;
using OpenRA.Server;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA.Widgets.Delegates
{
	public class MainMenuButtonsDelegate : IWidgetDelegate
	{
		static bool FirstInit = true;

		[ObjectCreator.UseCtor]
		public MainMenuButtonsDelegate([ObjectCreator.Param] Widget widget)
		{
			// Main menu is the default window
			widget.GetWidget("MAINMENU_BUTTON_JOIN").OnMouseUp = mi => { Widget.OpenWindow("JOINSERVER_BG"); return true; };
			widget.GetWidget("MAINMENU_BUTTON_CREATE").OnMouseUp = mi => { Widget.OpenWindow("CREATESERVER_BG"); return true; };
			widget.GetWidget("MAINMENU_BUTTON_SETTINGS").OnMouseUp = mi => { Widget.OpenWindow("SETTINGS_MENU"); return true; };
			widget.GetWidget("MAINMENU_BUTTON_MUSIC").OnMouseUp = mi => { Widget.OpenWindow("MUSIC_MENU"); return true; };
			widget.GetWidget("MAINMENU_BUTTON_REPLAY_VIEWER").OnMouseUp = mi => { Widget.OpenWindow("REPLAYBROWSER_BG"); return true; };
			widget.GetWidget("MAINMENU_BUTTON_QUIT").OnMouseUp = mi => { Game.Exit(); return true; };

			if (FirstInit)
			{
				FirstInit = false;
				Game.ConnectionStateChanged += orderManager =>
				{
					Widget.CloseWindow();
					switch( orderManager.Connection.ConnectionState )
					{
						case ConnectionState.PreConnecting:
							Widget.OpenWindow("MAINMENU_BG");
							break;
						case ConnectionState.Connecting:
							Widget.OpenWindow( "CONNECTING_BG",
								new Dictionary<string, object> { { "host", orderManager.Host }, { "port", orderManager.Port } } );
							break;
						case ConnectionState.NotConnected:
							Widget.OpenWindow( "CONNECTION_FAILED_BG",
								new Dictionary<string, object> { { "host", orderManager.Host }, { "port", orderManager.Port } } );
							break;
						case ConnectionState.Connected:
							var lobby = Widget.OpenWindow( "SERVER_LOBBY", new Dictionary<string, object> { { "orderManager", orderManager } } );
							lobby.GetWidget<ChatDisplayWidget>("CHAT_DISPLAY").ClearChat();
							lobby.GetWidget("CHANGEMAP_BUTTON").Visible = true;
							lobby.GetWidget("LOCKTEAMS_CHECKBOX").Visible = true;
							lobby.GetWidget("DISCONNECT_BUTTON").Visible = true;
							//r.GetWidget("INGAME_ROOT").GetWidget<ChatDisplayWidget>("CHAT_DISPLAY").ClearChat();	
							break;
					}
				};
			}
		}
	}
}
