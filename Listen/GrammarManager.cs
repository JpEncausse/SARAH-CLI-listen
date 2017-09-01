using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;

using SoftMade.IO;

#if MICRO
using System.Speech;
using System.Speech.Recognition;
using System.Speech.AudioFormat;
using System.Speech.Recognition.SrgsGrammar;
#endif

#if KINECT
using Microsoft.Speech;
using Microsoft.Speech.Recognition;
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition.SrgsGrammar;
#endif

namespace net.encausse.sarah {
    class GrammarManager : IDisposable {

        // -------------------------------------------
        //  SINGLETON
        // -------------------------------------------

        private static GrammarManager manager = null;
        private GrammarManager() { }

        public static GrammarManager GetInstance() {
            if (manager == null) {
                manager = new GrammarManager();
            }
            return manager;
        }

        public void Dispose() {
            Cache.Clear();
            if (watcher != null) {
                watcher.Dispose();
            }
        }

        // ------------------------------------------
        //  ENGINE
        // -----------------------------------------

        private SpeechEngine engine = null;
        private string language     = "fr-FR";
        private string hotword      = "SARAH";
        public void SetEngine(SpeechEngine engine, string language, string hotword) {
            this.engine = engine;
            this.language = language;
            this.hotword = hotword;
        }

        // ------------------------------------------
        //  UTILITY
        // ------------------------------------------

        protected void Log(string msg) {
            System.Diagnostics.Debug.WriteLine(msg);
        }

        // -------------------------------------------
        //  WATCHER
        // -------------------------------------------

        private AdvancedFileSystemWatcher watcher = null;
        private String path = null;

        public void Watch(string path) {
            if (!Directory.Exists(path)) { return; }
            Log("Watching: " + Path.GetFullPath(path));
            this.path = path;

            watcher = new AdvancedFileSystemWatcher();
            watcher.Path = path;
            watcher.Filter = "*.xml";
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Changed += new EventHandler<SoftMade.IO.FileSystemEventArgs>(watcher_Changed);
            watcher.EnableRaisingEvents = true;
        }

        protected void watcher_Changed(object sender, SoftMade.IO.FileSystemEventArgs e) {
            if (e.ChangeType == SoftMade.IO.WatcherChangeTypes.BeginWrite) { return; }
            watcher.EnableRaisingEvents = false;

            // Backup current context
            //var ctxt = ContextManager.GetInstance().Current;

            // Reload all grammar
            Load(path, 2);

            // Set context back
            //ContextManager.GetInstance().SetContext(ctxt);

            // Apply cache to all engines
            engine.Load(Cache, true);

            // Reset context timeout
            //ContextManager.GetInstance().ResetContextTimeout();
            watcher.EnableRaisingEvents = true;
        }

        // -------------------------------------------
        //  FIND
        // -------------------------------------------

        public SpeechGrammar FindGrammar(String name) {
            if (!Cache.ContainsKey(name)) { return null; }
            return Cache[name];
        }

        public String FindExample(String ruleId) {
            foreach (var g in Cache.Values) {
                if (!g.Examples.ContainsKey(ruleId)) { continue; }
                return g.Examples[ruleId];
            }
            return null;
        }

        // -------------------------------------------
        //  LOADER
        // -------------------------------------------

        public IDictionary<string, SpeechGrammar> Cache = new Dictionary<string, SpeechGrammar>();
        public void Load(string path, int depth) {
            if (depth <= 0) { return; }
            if (File.Exists(path)) { return; }
            Log("Searching: " + Path.GetFullPath(path));

            // Recursive with folders
            string[] folders = Directory.GetDirectories(path);
            foreach (string folder in folders) {
                Load(folder, depth - 1);
            }

            // Retrieve all .xml files
            string[] xml = Directory.GetFiles(path, "*.xml");
            foreach (string file in xml) {
                LoadFile(file);
            }
        }

        public void LoadFile(string path) {
            try {
                var name = Path.GetFileNameWithoutExtension(path);

                // Check Grammar cache
                SpeechGrammar grammar = FindGrammar(name);
                if (null != grammar) {
                    if (grammar.LastModified == File.GetLastWriteTime(path)) {
                        Log("Ignoring: " + name + " (no changes)");
                        return;
                    }
                }

                // New Grammar
                bool addToCache = false;
                if (null == grammar) {
                    grammar = new SpeechGrammar(name);
                    addToCache = true;
                }

                // Load XML
                string xml = File.ReadAllText(path, Encoding.UTF8);
                if (!LoadXML(grammar, xml)) { return; }

                // Add new grammar to cache
                if (addToCache) {
                    Cache.Add(name, grammar);
                }

                // Setup grammar
                grammar.Path = path;
                grammar.LastModified = File.GetLastWriteTime(path);

                // Check lazy
                grammar.Enabled = true;
                if ((path.IndexOf("lazy") >= 0) || Regex.IsMatch(xml, "root=\"lazy\\w+\"", RegexOptions.IgnoreCase)) {
                    grammar.Enabled = false;
                }

                // Check context
                // var ctx = ConfigManager.GetInstance().Find("speech.grammar.context", "");
                // if (!String.IsNullOrEmpty(ctx)) {
                //    var context = ctx.Split('|');
                //    if (context.Length > 0 && !Array.Exists(context, delegate (object s) { return s.Equals(name); })) {
                //        grammar.Enabled = false;
                //    }
                // }

                // Log
                Log("Loading: " + name + " (" + grammar.Enabled + ")" + " (" + path + ")");

                // Store default context
                //if (grammar.Enabled) {
                //    ContextManager.GetInstance().Default.Add(name);
                //    Log("Add to context list: " + name);
                //}
            }
            catch (Exception ex) {
                Log("Exception:\n" + ex.StackTrace);
            }
        }

        private bool LoadXML(SpeechGrammar grammar, string xml) {
            // Check Language
            if (!Regex.IsMatch(xml, "xml:lang=\"" + language + "\"", RegexOptions.IgnoreCase)) {
                Log("Ignoring : " + grammar.Name + " (" + language + ")");
                return false;
            }

            // Clean XML
            var bot = hotword.ToUpper();

            // Replace SaRaH by bot name
            xml = Regex.Replace(xml, "([^/])SARAH", "$1" + bot, RegexOptions.IgnoreCase);

            // Add optional SARAH
            var item = "<item>\\s*" + bot + "\\s*</item>";
            if (Regex.IsMatch(xml, item, RegexOptions.IgnoreCase)) {
                xml = Regex.Replace(xml, item, "<item repeat=\"0-1\">" + bot + "</item>", RegexOptions.IgnoreCase);
                grammar.HasName = true;
            }

            // Set XML
            grammar.SetXML(xml);
            return true;
        }

        // -------------------------------------------
        //  UPDATE
        // -------------------------------------------

        public SpeechGrammar UpdateXML(String name, String bodyXML, bool ms) {
            var grammar = FindGrammar(name);
            return UpdateXML(grammar, bodyXML, ms);
        }

        public SpeechGrammar UpdateXML(SpeechGrammar grammar, String bodyXML, bool ms) {
            if (grammar == null) { return null; }
            var o = ms ? "$" : "out";

            // Include BODY to XML
            var name = grammar.Name;
            var rule = "rule" + Char.ToUpper(name[0]) + name.Substring(1);
            var xml = "\n<grammar version=\"1.0\" xml:lang=\"" + language + "\" mode=\"voice\"  root=\"" + name + "\" xmlns=\"http://www.w3.org/2001/06/grammar\" tag-format=\"" + (ms ? "semantics-ms/1.0" : "semantics/1.0") + "\">";
            xml += "\n<rule id=\"" + name + "\" scope=\"public\">";
            xml += "\n<tag>" + o + ".action=new Object(); </tag>";
            xml += bodyXML;
            xml += "\n</rule>";
            xml += "\n</grammar>";

            // Load Grammar
            LoadXML(grammar, xml);
            grammar.LastModified = DateTime.Now;

            // Add to cache
            if (!Cache.ContainsKey(name)) {
                Cache[name] = grammar;
            }

            // Reload the XML of the Grammar
            engine.Load(name, grammar.Build());

            return grammar;
        }

        // -------------------------------------------
        //  APPLY GRAMMAR STATE
        // -------------------------------------------

        public void ApplyGrammarsToEngines() {
            // Log("Forward Context enable/disable grammar");
            foreach (Grammar g in engine.Engine.Grammars) {
                if (!Cache.ContainsKey(g.Name))
                    continue;
                g.Enabled = Cache[g.Name].Enabled;
                // Log("ApplyGrammarsToEngines: " + g.Name + " = " + g.Enabled);
            }
        }
    }

    // ====================================================================================
    //  INNER CLASS
    // ====================================================================================

    public class SpeechGrammar {

        public String Name { get; set; }
        public SpeechGrammar(string name) {
            this.Name = name;
        }

        public bool HasName { get; set; }
        public bool Enabled { get; set; }
        public bool Wildcard { get; set; }
        public String Wrong { get; set; }
        public String Path { get; set; }
        public String CallbackURL { get; set; }
        public DateTime LastModified { get; set; }
        public IDictionary<string, string> Examples { get; set; }

        private String xml = null;
        public void SetXML(string xml) {
            this.xml = xml;

            // Seek for examples
            Examples = new Dictionary<string, string>();

            var doc = XDocument.Parse(xml);
            for (var rule = doc.Root.FirstNode ; rule != null ; rule = rule.NextNode) {
                if (rule is XComment) { continue; }
                XElement elem = (XElement) rule;

                var xId = elem.Attribute("id");
                if (xId == null)
                    continue;

                var id = xId.Value;
                if (id == null)
                    continue;

                var children = elem.Elements();
                if (children == null)
                    continue;

                foreach (var xEx in children) {
                    if (xEx.Name.Equals("example")) { continue; }
                    var example = xEx.Value;
                    Examples.Add(id, example);
                    break;
                }
            }
        }

        /**
         * Build a new Grammar for each Emgine
         * See: msdn.microsoft.com/en-us/library/hh362887(v=office.14).aspx
         */

        public Grammar Build() {
            if (xml == null) { return null; }
            using (Stream s = StreamFromString(xml)) {
                try {
                    Grammar grammar = new Grammar(s);
                    grammar.Enabled = Enabled;
                    grammar.Name = Name;
                    return grammar;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.StackTrace); } // LOG
            }
            return null;
        }

        private Stream StreamFromString(string s) {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
