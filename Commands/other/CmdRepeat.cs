/*
    Copyright 2011 MCGalaxy
        
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
namespace MCGalaxy.Commands {
    
    public sealed class CmdRepeat : Command {
        
        public override string name { get { return "repeat"; } }
        public override string shortcut { get { return ""; } }
        public override string type { get { return CommandTypes.Other; } }
        public override bool museumUsable { get { return false; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Guest; } }
        public CmdRepeat() { }

        public override void Use(Player p, string message) {
            if (p.lastCMD == "") { Player.SendMessage(p, "No commands used yet."); return; }
            if (p.lastCMD.Length > 5 && p.lastCMD.Substring(0, 6) == "static") {
                Player.SendMessage(p, "Can't repeat static"); return;
            }

            Player.SendMessage(p, "Using &b/" + p.lastCMD);
            int argsIndex = p.lastCMD.IndexOf(' ');
            string cmdName = argsIndex == -1 ? p.lastCMD : p.lastCMD.Substring(0, argsIndex);
            string cmdMsg = argsIndex == -1 ? "" : p.lastCMD.Substring(argsIndex + 1);
            
            Command cmd = Command.all.Find(cmdName);
            if (cmd == null) {
                Player.SendMessage(p, "Unknown command \"" + cmdName + "\".");
            }
            if (p != null && !p.group.CanExecute(cmd)) {
                Player.SendMessage(p, "You are not allowed to use \"" + cmdName + "\"."); return;
            }
            cmd.Use(p, cmdMsg);
        }
        
        public override void Help(Player p) {
            Player.SendMessage(p, "%T/repeat");
            Player.SendMessage(p, "%HRepeats the last used command");
        }
    }
}
