using NAudio.Wave;
using System;
using System.IO;
using System.Collections.Generic;
using System.Speech.AudioFormat;
using Mono.Options;

namespace net.encausse.sarah { 
    class Program {
        static void Main(string[] args) {
            
            string device = "";
            string recognizer = "";
            string language = "fr-FR";
            string hotword = "SARAH";
            double confidence = 0.5;
            int deviceId = -1;
            var grammar = "";


            bool help = false;
            var p = new OptionSet() {
                { "device=", "the device id", v => device = v },
                { "recognizer=", "the recognizer id", v => recognizer = v },
                { "language=", "the recognizer language", v => language = v },
                { "grammar=", "the grammar directory", v => grammar = v },
                { "hotword=", "the hotword (default is SARAH)", v => hotword = v },
                { "confidence=", "the reconizer confidence", v => confidence = Double.Parse(v) },
                { "h|help",  "show this message and exit", v => help = v != null },
            };

            List<string> extra;
            try { extra = p.Parse(args); }
            catch (OptionException e) {
                Console.Write("Listen: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `Listen --help' for more information.");
                return;
            }

            if (help) {
                ShowHelp(p);
                return;
            }

            // Create Speech Engine & Grammar Manager
            SpeechEngine engine = new SpeechEngine(device, recognizer, language, confidence);
            GrammarManager.GetInstance().SetEngine(engine, language, hotword);

            grammar = Path.GetFullPath(grammar);
            GrammarManager.GetInstance().Load(grammar, 2);
            GrammarManager.GetInstance().Watch(grammar);

            engine.Load(GrammarManager.GetInstance().Cache, false);
            engine.Init();


            // Create Stream
            var buffer = new StreamBuffer();
            var waveIn = new WaveInEvent();
            waveIn.DeviceNumber = deviceId;
            waveIn.WaveFormat = new WaveFormat(16000, 2);
            waveIn.DataAvailable += (object sender, WaveInEventArgs e) => {
                lock (buffer) {
                    var pos = buffer.Position;
                    buffer.Write(e.Buffer, 0, e.BytesRecorded);
                    buffer.Position = pos;
                }
            };
            waveIn.StartRecording();

            // Pipe Stream and start
            var info = new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Stereo);
            engine.SetInputToAudioStream(buffer, info);
            engine.Start();

            // Prevent console from closing
            Console.WriteLine("Waiting for key pressed...");
            Console.ReadLine();
        }

        static void ShowHelp(OptionSet p) {
            Console.WriteLine("Usage: Listen [OPTIONS]+ path");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }
    }
}
