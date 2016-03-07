﻿/*
Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/MCGalaxy)
Dual-licensed under the Educational Community License, Version 2.0 and
the GNU General Public License, Version 3 (the "Licenses"); you may
not use this file except in compliance with the Licenses. You may
obtain a copy of the Licenses at
http://www.opensource.org/licenses/ecl2.php
http://www.gnu.org/licenses/gpl-3.0.html
Unless required by applicable law or agreed to in writing,
software distributed under the Licenses are distributed on an "AS IS"
BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
or implied. See the Licenses for the specific language governing
permissions and limitations under the Licenses.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MCGalaxy.Drawing;
using MCGalaxy.SQL;

namespace MCGalaxy {
    public sealed partial class Player : IDisposable {

        public NetworkStream Stream;
        public BinaryReader Reader;

        static void Receive(IAsyncResult result) {
            //Server.s.Log(result.AsyncState.ToString());
            Player p = (Player)result.AsyncState;
            if ( p.disconnected || p.socket == null )
                return;
            try {
                int length = p.socket.EndReceive(result);
                if ( length == 0 ) { p.Disconnect(); return; }

                byte[] b = new byte[p.buffer.Length + length];
                Buffer.BlockCopy(p.buffer, 0, b, 0, p.buffer.Length);
                Buffer.BlockCopy(p.tempbuffer, 0, b, p.buffer.Length, length);

                p.buffer = p.HandleMessage(b);
                if ( p.dontmindme && p.buffer.Length == 0 ) {
                    Server.s.Log("Disconnected");
                    p.socket.Close();
                    p.disconnected = true;
                    return;
                }
                if ( !p.disconnected )
                    p.socket.BeginReceive(p.tempbuffer, 0, p.tempbuffer.Length, SocketFlags.None,
                                          new AsyncCallback(Receive), p);
            }catch ( SocketException ) {
                p.Disconnect();
            }  catch ( ObjectDisposedException ) {
                // Player is no longer connected, socket was closed
                // Mark this as disconnected and remove them from active connection list
                Player.SaveUndo(p);
                connections.Remove(p);
                p.RemoveFromPending();
                p.disconnected = true;
            } catch ( Exception e ) {
                Server.ErrorLog(e);
                p.Kick("Error!");
            }
        }
        
        public bool hasCpe = false, hasCustomBlocks = false, hasTextColors, finishedCpeLogin = false;
        public string appName;
        public int extensionCount;
        public List<string> extensions = new List<string>();
        public int customBlockSupportLevel;
        void HandleExtInfo( byte[] message ) {
            appName = enc.GetString( message, 0, 64 ).Trim();
            extensionCount = message[65];
            // NOTE: Workaround as ClassiCube violates the CPE specification here.
            // If server sends version 2, the client should reply with version 1.
            // Except ClassiCube just doesn't reply at all if server sends version 2.
            if (appName == "ClassiCube Client")
                EnvMapAppearance = 1;
        }

        void HandleExtEntry( byte[] message ) {
            AddExtension(enc.GetString(message, 0, 64).Trim(), NetUtils.ReadI32(message, 64));
            extensionCount--;
            if (extensionCount <= 0 && !finishedCpeLogin) {
            	CompleteLoginProcess();
            	finishedCpeLogin = true;
            }
        }

        void HandleCustomBlockSupportLevel( byte[] message ) {
            customBlockSupportLevel = message[0];
        }
        
        char[] characters = new char[64];
        string GetString( byte[] data, int offset ) {
            int length = 0;
            for( int i = 63; i >= 0; i-- ) {
                byte code = data[i + offset];
                if( length == 0 && !( code == 0 || code == 0x20 ) )
                    length = i + 1;
                characters[i] = (char)code;
            }
            return new String( characters, 0, length );
        }

        public void SendRaw(int id) {
        	byte[] buffer = new [] { (byte)id };
        	SendRaw(buffer);
        }
        
        public void SendRaw(int id, byte data) {
        	byte[] buffer = new [] { (byte)id, data };
        	SendRaw(buffer);
        }
        
        [Obsolete("Include the opcode in the array to avoid an extra temp allocation.")]
        public void SendRaw(int id, byte[] send, bool sync = false) {
            byte[] buffer = new byte[send.Length + 1];
            buffer[0] = (byte)id;
            for ( int i = 0; i < send.Length; i++ )
                buffer[i + 1] = send[i];
            SendRaw(buffer, sync);
            buffer = null;
        }
        
        public void SendRaw(byte[] buffer, bool sync = false) {
            // Abort if socket has been closed
            if (socket == null || !socket.Connected) return;
            
            try {
                if (sync)
                    socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
                else
                    socket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, delegate(IAsyncResult result) { }, null);
                buffer = null;
            } catch (SocketException e) {
            	buffer = null;
                Disconnect();
                #if DEBUG
                Server.ErrorLog(e);
                #endif
            }
        }
        
        public void SendBlankMessage() {
        	byte[] buffer = new byte[66];
            buffer[0] = Opcode.Message;
            NetUtils.WriteAscii("", buffer, 2);
            SendRaw(buffer);
        }

        public static void SendMessage(Player p, string message) {
            SendMessage(p, message, true);
        }
        
        public static void SendMessage(Player p, string message, bool colorParse) {
            if (p == null) {
                if (storeHelp)
                    storedHelp += message + "\r\n";
                else
                    Server.s.Log(message);
            } else if (p.name == "IRC") {
                if (String.IsNullOrEmpty(Server.IRC.usedCmd))
                    Server.IRC.Say(message, false, true);
                else
                    Server.IRC.Pm(Server.IRC.usedCmd, message);
            } else {
                p.SendMessage(0, Server.DefaultColor + message, colorParse);
            }
        }
        
        public void SendMessage(string message) {
           SendMessage(0, Server.DefaultColor + message, true);
        }
        
        public void SendMessage(string message, bool colorParse) {
            SendMessage(0, Server.DefaultColor + message, colorParse);
        }
        
        public void SendMessage(byte id, string message, bool colorParse = true) {
            if (colorParse)
            	message = Colors.EscapeColors(message);
            StringBuilder sb = new StringBuilder(message);

            if (colorParse) {
            	for (int i = 0; i < 128; i++) {
            		if (Colors.IsStandardColor((char)i)) {
            		    if (i >= 'A' && i <= 'F') // WoM does not work with uppercase color codes.
            		        sb.Replace("&" + (char)i, "&" + (char)(i + ' '));
            		    continue;
            		}
            		
            		CustomColor col = Colors.ExtColors[i];               
                    if (col.Undefined) {
                        sb.Replace("&" + (char)i, ""); continue;
                    }
                    if (!hasTextColors) {
                        sb.Replace("&" + (char)i, "&" + col.Fallback); continue;
                    }
            	}
            }
            
            Chat.ApplyTokens(sb, this, colorParse);
            if ( Server.parseSmiley && parseSmiley ) {
                sb.Replace(":)", "(darksmile)");
                sb.Replace(":D", "(smile)");
                sb.Replace("<3", "(heart)");
            }

            message = EmotesHandler.ReplaceEmoteKeywords(sb.ToString());
            message = FullCP437Handler.Replace(message);
            int totalTries = 0;
            if ( MessageRecieve != null )
                MessageRecieve(this, message);
            if ( OnMessageRecieve != null )
                OnMessageRecieve(this, message);
            OnMessageRecieveEvent.Call(this, message);
            if ( cancelmessage ) {
                cancelmessage = false;
                return;
            }
            retryTag: try {
                foreach ( string line in Wordwrap(message) ) {
                    string newLine = line;
                    if ( newLine.TrimEnd(' ')[newLine.TrimEnd(' ').Length - 1] < '!' ) {
                        if (!HasCpeExt(CpeExt.EmoteFix))
                            newLine += '\'';
                    }
                    
                    byte[] buffer = new byte[66];
                    buffer[0] = Opcode.Message;
                    buffer[1] = id;
                    if(HasCpeExt(CpeExt.FullCP437))
                    	NetUtils.WriteCP437(newLine, buffer, 2);
                    else
                        NetUtils.WriteAscii(newLine, buffer, 2);
                    SendRaw(buffer);
                }
            } catch ( Exception e ) {
                message = "&f" + message;
                totalTries++;
                if ( totalTries < 10 ) goto retryTag;
                else Server.ErrorLog(e);
            }
        }

        public void SendMotd() {
            byte[] buffer = new byte[131];
            buffer[0] = Opcode.Handshake;
            buffer[1] = (byte)8;
            NetUtils.WriteAscii(Server.name, buffer, 2);

            if ( !String.IsNullOrEmpty(group.MOTD) )
                NetUtils.WriteAscii(group.MOTD, buffer, 66);
            else
                NetUtils.WriteAscii(Server.motd, buffer, 66);

            bool canPlace = Block.canPlace(this, Block.blackrock);
            buffer[130] = canPlace ? (byte)100 : (byte)0;
            if (OnSendMOTD != null) OnSendMOTD(this, buffer);
            SendRaw(buffer);
        }

        public void SendUserMOTD() {
            byte[] buffer = new byte[131];
            buffer[0] = Opcode.Handshake;
            buffer[1] = Server.version;

            if (level.motd == "ignore") {
                NetUtils.WriteAscii(Server.name, buffer, 2);
                if (!String.IsNullOrEmpty(group.MOTD) ) 
                	NetUtils.WriteAscii(group.MOTD, buffer, 66);
                else 
                	NetUtils.WriteAscii(Server.motd, buffer, 66);
            } else {
            	NetUtils.WriteAscii(level.motd, buffer, 2);
            	if (level.motd.Length > 64)
            		NetUtils.WriteAscii(level.motd.Substring(64), buffer, 66);
            }

            bool canPlace = Block.canPlace(this, Block.blackrock);
            buffer[130] = canPlace ? (byte)100 : (byte)0;
            SendRaw(buffer);
        }

        public void SendMap(Level oldLevel) { SendRawMap(oldLevel, level); }
        
        public bool SendRawMap(Level oldLevel, Level level) {
            if (level.blocks == null) return false;
            bool success = true;
            useCheckpointSpawn = false;
            lastCheckpointIndex = -1;
            
            try {
                int usedLength = 0;
                byte[] buffer = CompressRawMap(out usedLength);
                
                if (HasCpeExt(CpeExt.BlockDefinitions)) {
                    if (oldLevel != null && oldLevel != level)
                        RemoveOldLevelCustomBlocks(oldLevel);
                    BlockDefinition.SendLevelCustomBlocks(this);
                }
                
                SendRaw(Opcode.LevelInitialise);
                int totalRead = 0;                
                while (totalRead < usedLength) {   
                    byte[] packet = new byte[1028]; // need each packet separate for Mono
                    packet[0] = Opcode.LevelDataChunk;
                    short length = (short)Math.Min(buffer.Length - totalRead, 1024);
                    NetUtils.WriteI16(length, packet, 1);
                    Buffer.BlockCopy(buffer, totalRead, packet, 3, length);
                    packet[1027] = (byte)(100 * (float)totalRead / buffer.Length);
                    
                    SendRaw(packet);            
                    if (ip != "127.0.0.1")
                        Thread.Sleep(Server.updateTimer.Interval > 1000 ? 100 : 10);
                    totalRead += length;
                }
                
                buffer = new byte[7];
                buffer[0] = Opcode.LevelFinalise;
                NetUtils.WriteI16((short)level.Width, buffer, 1);
                NetUtils.WriteI16((short)level.Height, buffer, 3);
                NetUtils.WriteI16((short)level.Length, buffer, 5);
                SendRaw(buffer);
                Loading = false;
                
                if (HasCpeExt(CpeExt.EnvWeatherType))
                    SendSetMapWeather(level.weather);
                if (HasCpeExt(CpeExt.EnvColors))
                    SendCurrentEnvColors();
                if (HasCpeExt(CpeExt.EnvMapAppearance) || HasCpeExt(CpeExt.EnvMapAppearance, 2))
                    SendCurrentMapAppearance();
                
                if ( OnSendMap != null )
                    OnSendMap(this, buffer);
                if (!level.guns)
                    aiming = false;
            } catch( Exception ex ) {
                success = false;
                Command.all.Find("goto").Use(this, Server.mainLevel.name);
                SendMessage("There was an error sending the map data, you have been sent to the main level.");
                Server.ErrorLog(ex);
            } finally {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            
            if (HasCpeExt(CpeExt.BlockPermissions))
                SendCurrentBlockPermissions();
            return success;
        }
        
        byte[] CompressRawMap(out int usedLength) {
            const int bufferSize = 64 * 1024;
            byte[] buffer = new byte[bufferSize];
            MemoryStream temp = new MemoryStream();
            int bIndex = 0;
            bool hasBlockDefs = HasCpeExt(CpeExt.BlockDefinitions);
            
            using (GZipStream compressor = new GZipStream(temp, CompressionMode.Compress, true)) {
                NetUtils.WriteI32(level.blocks.Length, buffer, 0);
                compressor.Write(buffer, 0, sizeof(int));
                
                // compress the map data in 64 kb chunks
                if (hasCustomBlocks) {
                    for (int i = 0; i < level.blocks.Length; ++i) {
                        byte block = level.blocks[i];
                        if (block == Block.custom_block) {
                            if (hasBlockDefs) buffer[bIndex] = level.GetExtTile(i);
                            else buffer[bIndex] = level.GetFallbackExtTile(i);
                        } else {
                            buffer[bIndex] = Block.Convert(block);
                        }
                        
                        bIndex++;
                        if (bIndex == bufferSize) {
                            compressor.Write(buffer, 0, bufferSize); bIndex = 0;
                        }
                    }
                } else {
                    for (int i = 0; i < level.blocks.Length; ++i) {
                        byte block = level.blocks[i];
                        if (block == Block.custom_block) {
                            if (hasBlockDefs) buffer[bIndex] = Block.ConvertCPE(level.GetExtTile(i));
                            else buffer[bIndex] = Block.ConvertCPE(level.GetFallbackExtTile(i));
                        } else {
                            buffer[bIndex] = Block.Convert(Block.ConvertCPE(level.blocks[i]));
                        }
                        
                        bIndex++;
                        if (bIndex == bufferSize) {
                            compressor.Write(buffer, 0, bufferSize); bIndex = 0;
                        }
                    }
                }               
                if (bIndex > 0) compressor.Write(buffer, 0, bIndex);
            }
            usedLength = (int)temp.Length;
            return temp.GetBuffer();
        }
        
        void RemoveOldLevelCustomBlocks(Level oldLevel) {
        	BlockDefinition[] defs = oldLevel.CustomBlockDefs;
        	for (int i = Block.CpeCount; i < 256; i++) {
        		BlockDefinition def = defs[i];
        		if (def == null || def == BlockDefinition.GlobalDefs[i]) continue;
        		SendRaw(Opcode.CpeRemoveBlockDefinition, (byte)i);
        	}
        }
        
        public void SendSpawn(byte id, string name, ushort x, ushort y, ushort z, byte rotx, byte roty) {
            byte[] buffer = new byte[74];
            buffer[0] = Opcode.AddEntity;
            buffer[1] = id;
            NetUtils.WriteAscii(name.TrimEnd('+'), buffer, 2);
            NetUtils.WriteU16(x, buffer, 66);
            NetUtils.WriteU16(y, buffer, 68);
            NetUtils.WriteU16(z, buffer, 70);
            buffer[72] = rotx; 
            buffer[73] = roty;
            SendRaw(buffer);

            if (HasCpeExt(CpeExt.ChangeModel))
            	UpdateModels();
        }
        
        public void SendPos(byte id, ushort x, ushort y, ushort z, byte rotx, byte roty) {
            if ( x < 0 ) x = 32;
            if ( y < 0 ) y = 32;
            if ( z < 0 ) z = 32;
            if ( x > level.Width * 32 ) x = (ushort)( level.Width * 32 - 32 );
            if ( z > level.Length * 32 ) z = (ushort)( level.Length * 32 - 32 );
            if ( x > 32767 ) x = 32730;
            if ( y > 32767 ) y = 32730;
            if ( z > 32767 ) z = 32730;

            pos[0] = x; pos[1] = y; pos[2] = z;
            rot[0] = rotx; rot[1] = roty;

            byte[] buffer = new byte[10]; 
            buffer[0] = Opcode.EntityTeleport;
            buffer[1] = id;
            NetUtils.WriteU16(x, buffer, 2);
            NetUtils.WriteU16(y, buffer, 4);
            NetUtils.WriteU16(z, buffer, 6);
            buffer[8] = rotx; 
            buffer[9] = roty;
            SendRaw(buffer);
        }
        
        public void SendUserType(bool op) {
            SendRaw(Opcode.SetPermission, op ? (byte)100 : (byte)0);
        }
        
        //TODO: Figure a way to SendPos without changing rotation
        public void SendDespawn(byte id) { 
        	SendRaw(Opcode.RemoveEntity, id); 
        }
        
        public void SendBlockchange(ushort x, ushort y, ushort z, byte type) {
            if (x < 0 || y < 0 || z < 0) return;
            if (x >= level.Width || y >= level.Height || z >= level.Length) return;

            byte[] buffer = new byte[8];
            buffer[0] = Opcode.SetBlock;
            NetUtils.WriteU16(x, buffer, 1);
            NetUtils.WriteU16(y, buffer, 3);
            NetUtils.WriteU16(z, buffer, 5);
            
            if (type == Block.custom_block) {
            	if (HasCpeExt(CpeExt.BlockDefinitions))
            		buffer[7] = level.GetExtTile(x, y, z);
            	else
            		buffer[7] = level.GetFallbackExtTile(x, y, z);
            } else if (hasCustomBlocks) {
            	buffer[7] = Block.Convert(type);
            } else {
            	buffer[7] = Block.Convert(Block.ConvertCPE(type));
            }
            SendRaw(buffer);
        }
        
        // Duplicated as this packet needs to have maximum optimisation.
        public void SendBlockchange(ushort x, ushort y, ushort z, byte type, byte extType) {
            if (x < 0 || y < 0 || z < 0) return;
            if (x >= level.Width || y >= level.Height || z >= level.Length) return;

            byte[] buffer = new byte[8];
            buffer[0] = Opcode.SetBlock;
            NetUtils.WriteU16(x, buffer, 1);
            NetUtils.WriteU16(y, buffer, 3);
            NetUtils.WriteU16(z, buffer, 5);
            
            if (type == Block.custom_block) {
            	if (HasCpeExt(CpeExt.BlockDefinitions))
            		buffer[7] = extType;
            	else
            		buffer[7] = level.GetFallback(extType);
            } else if (hasCustomBlocks) {
            	buffer[7] = Block.Convert(type);
            } else {
            	buffer[7] = Block.Convert(Block.ConvertCPE(type));
            }
            SendRaw(buffer);
        }
        
        void SendKick(string message, bool sync) {
        	byte[] buffer = new byte[65];
        	buffer[0] = Opcode.Kick;
        	NetUtils.WriteAscii(message, buffer, 1);
        	SendRaw(buffer, sync); 
        }
        
        void SendPing() { 
        	SendRaw(Opcode.Ping);
        }
        
        void SendExtInfo( byte count ) {
            byte[] buffer = new byte[67];
            buffer[0] = Opcode.CpeExtInfo;
            NetUtils.WriteAscii("MCGalaxy " + Server.Version, buffer, 1);
            NetUtils.WriteI16((short)count, buffer, 65);
            SendRaw(buffer, true);
        }
        
        void SendExtEntry( string name, int version ) {
        	byte[] buffer = new byte[69];
        	buffer[0] = Opcode.CpeExtEntry;
            NetUtils.WriteAscii(name, buffer, 1);
            NetUtils.WriteI32(version, buffer, 65);
            SendRaw(buffer, true);
        }
        
       public void SendClickDistance( short distance ) {
            byte[] buffer = new byte[3];
            buffer[0] = Opcode.CpeSetClickDistance;
            NetUtils.WriteI16(distance, buffer, 1);
            SendRaw(buffer);
        }
        
        void SendCustomBlockSupportLevel(byte level) {
            SendRaw(Opcode.CpeCustomBlockSupportLevel, level);
        }
        
        void SendHoldThis( byte type, byte locked ) { // if locked is on 1, then the player can't change their selected block.
            byte[] buffer = new byte[3];
            buffer[0] = Opcode.CpeHoldThis;
            buffer[1] = type;
            buffer[2] = locked;
            SendRaw(buffer);
        }
        
        void SendTextHotKey( string label, string command, int keycode, byte mods ) {
            byte[] buffer = new byte[134];
            buffer[0] = Opcode.CpeSetTextHotkey;
            NetUtils.WriteAscii(label, buffer, 1);
            NetUtils.WriteAscii(command, buffer, 65);
            NetUtils.WriteI32(keycode, buffer, 129);
            buffer[133] = mods;
            SendRaw(buffer);
        }
        
        public void SendExtAddPlayerName(short id, string name, Group grp, string displayname = "") {
            byte[] buffer = new byte[196];
            buffer[0] = Opcode.CpeExtAddPlayerName;
            NetUtils.WriteI16(id, buffer, 1);
            NetUtils.WriteAscii(name, buffer, 3);
            if (displayname == "") displayname = name;
            NetUtils.WriteAscii(displayname, buffer, 67);
            NetUtils.WriteAscii(grp.color + grp.name.ToUpper() + "s:", buffer, 131);
            buffer[195] = (byte)grp.Permission.GetHashCode();
            SendRaw(buffer);
        }

        public void SendExtAddEntity(byte id, string name, string displayname = "") {
            byte[] buffer = new byte[130];
            buffer[0] = Opcode.CpeExtAddEntity;
            buffer[1] = id;
            NetUtils.WriteAscii(name, buffer, 2);
            if (displayname == "") displayname = name;
            NetUtils.WriteAscii(displayname, buffer, 66);
            SendRaw(buffer);
        }
        
        public void SendDeletePlayerName( byte id ) {
            byte[] buffer = new byte[3];
            buffer[0] = Opcode.CpeExtRemovePlayerName;
            NetUtils.WriteI16(id, buffer, 1);
            SendRaw(buffer);
        }
        
        public void SendEnvColor( byte type, short r, short g, short b ) {
            byte[] buffer = new byte[8];
            buffer[0] = Opcode.CpeEnvColors;
            buffer[1] = type;
            NetUtils.WriteI16( r, buffer, 2 );
            NetUtils.WriteI16( g, buffer, 4 );
            NetUtils.WriteI16( b, buffer, 6 );
            SendRaw(buffer);
        }
        
        public void SendMakeSelection( byte id, string label, short smallx, short smally, short smallz, short bigx, short bigy, short bigz, short r, short g, short b, short opacity ) {
            byte[] buffer = new byte[86];
            buffer[0] = Opcode.CpeMakeSelection;
            buffer[1] = id;
            NetUtils.WriteAscii(label, buffer, 2);
            NetUtils.WriteI16(smallx, buffer, 66);
            NetUtils.WriteI16(smally, buffer, 68);
            NetUtils.WriteI16(smallz, buffer, 70);
            NetUtils.WriteI16(bigx, buffer, 72);
            NetUtils.WriteI16(bigy, buffer, 74);
            NetUtils.WriteI16(bigz, buffer, 76);
            NetUtils.WriteI16(r, buffer, 78);
            NetUtils.WriteI16(g, buffer, 80);
            NetUtils.WriteI16(b, buffer, 82);
            NetUtils.WriteI16(opacity, buffer, 84);
            SendRaw(buffer);
        }
        
        public void SendDeleteSelection( byte id ) {
            SendRaw(Opcode.CpeRemoveSelection, id);
        }
        
        public void SendSetBlockPermission( byte type, bool canplace, bool candelete ) {
            byte[] buffer = new byte[4];
            buffer[0] = Opcode.CpeSetBlockPermission;
            buffer[1] = type;
            buffer[2] = canplace ? (byte)1 : (byte)0;
            buffer[3] = candelete ? (byte)1 : (byte)0;
            SendRaw(buffer);
        }
        
        public void SendChangeModel( byte id, string model ) {
            byte[] buffer = new byte[66];
            buffer[0] = Opcode.CpeChangeModel;
            buffer[1] = id;
            NetUtils.WriteAscii(model, buffer, 2);
            SendRaw(buffer);
        }
        
        public void SendSetMapAppearance( string url, byte sideblock, byte edgeblock, short sidelevel ) {
        	byte[] buffer = new byte[69];
        	buffer[0] = Opcode.CpeEnvSetMapApperance;
            NetUtils.WriteAscii(url, buffer, 1);
            buffer[65] = sideblock;
            buffer[66] = edgeblock;
            NetUtils.WriteI16(sidelevel, buffer, 67);
            SendRaw(buffer);
        }
        
        public void SendSetMapAppearanceV2( string url, byte sideblock, byte edgeblock, short sidelevel, 
                                           short cloudHeight, short maxFog ) {
        	byte[] buffer = new byte[73];
        	buffer[0] = Opcode.CpeEnvSetMapApperance;
            NetUtils.WriteAscii(url, buffer, 1);
            buffer[65] = sideblock;
            buffer[66] = edgeblock;
            NetUtils.WriteI16(sidelevel, buffer, 67);
            NetUtils.WriteI16(cloudHeight, buffer, 69);
            NetUtils.WriteI16(maxFog, buffer, 71);
            SendRaw(buffer);
        }
        
        public void SendSetMapWeather( byte weather ) { // 0 - sunny; 1 - raining; 2 - snowing
            SendRaw(Opcode.CpeEnvWeatherType, weather);
        }
        
        void SendHackControl( byte allowflying, byte allownoclip, byte allowspeeding, byte allowrespawning, 
                             byte allowthirdperson, short maxjumpheight ) {
            byte[] buffer = new byte[8];
            buffer[0] = Opcode.CpeHackControl;
            buffer[1] = allowflying;
            buffer[2] = allownoclip;
            buffer[3] = allowspeeding;
            buffer[4] = allowrespawning;
            buffer[5] = allowthirdperson;
            NetUtils.WriteI16(maxjumpheight, buffer, 6);
            SendRaw(buffer);
        }
        
        void UpdatePosition() {
        	//pingDelayTimer.Stop();
        	byte[] packet = NetUtils.GetPositionPacket(id, pos, oldpos, rot, oldrot, MakePitch(), false);
        	oldpos = pos; oldrot = rot;
        	if (packet == null) return;
        	
        	try {
        		foreach (Player p in PlayerInfo.players) {
        			if (p != this && p.level == level)
        				p.SendRaw(packet);
        		}
        	} catch { }
        }
        
        byte MakePitch() {
        	if (Server.flipHead || (flipHead && infected))
        		if (rot[1] > 64 && rot[1] < 192)
        			return rot[1];
        		else
        			return 128;
        	return rot[1];
        }

        internal void CloseSocket() {
            // Try to close the socket.
            // Sometimes its already closed so these lines will cause an error
            // We just trap them and hide them from view :P
            try {
                // Close the damn socket connection!
                socket.Shutdown(SocketShutdown.Both);
                #if DEBUG
                Server.s.Log("Socket was shutdown for " + this.name ?? this.ip);
                #endif
            }
            catch ( Exception e ) {
                #if DEBUG
                Exception ex = new Exception("Failed to shutdown socket for " + this.name ?? this.ip, e);
                Server.ErrorLog(ex);
                #endif
            }

            try {
                socket.Close();
                #if DEBUG
                Server.s.Log("Socket was closed for " + this.name ?? this.ip);
                #endif
            }
            catch ( Exception e ) {
                #if DEBUG
                Exception ex = new Exception("Failed to close socket for " + this.name ?? this.ip, e);
                Server.ErrorLog(ex);
                #endif
            }
            RemoveFromPending();
        }

        public string ReadString(int count = 64) {
            if ( Reader == null ) return null;
            var chars = new byte[count];
            Reader.Read(chars, 0, count);
            return Encoding.UTF8.GetString(chars).TrimEnd().Replace("\0", string.Empty);

        }
    }
}
