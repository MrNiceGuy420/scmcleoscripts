﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace asm {
	public partial class Form1 : Form {

		[STAThread]
		static void Main(string[] args) {
			string result = "";
			if (args.Length > 0) {
				result = doheadless(args[0]);
				if (result == null) {
					return;
				}
			}

			Application.SetCompatibleTextRenderingDefault(true);
			Application.Run(new Form1(result));
		}

		private static string headlessTargetFile = null;
		private static string gcc;
		private static string objdump;
		private static bool comments;
		private static bool correctoffsets = true;
		private static bool jump = true;
		private static string hookaddr;

		public Form1(string initialText) {
			InitializeComponent();
			textBox1.Text = initialText;
		}

		private static string doheadless(string file) {
			if (!File.Exists(file)) {
				return "input file doesn't exist";
			}

			List<string> lines = new List<string>();
			using (var reader = new StreamReader(file)) {
				string line;
				while ((line = reader.ReadLine()) != null) {
					if (line.StartsWith(";_ASM_") || line.StartsWith("; _ASM_")) {
						string directive = line.Substring(";_ASM_".Length);
						if (directive[0] == '_') {
							directive = directive.Substring(1);
						}
						parseDirective(directive);
						continue;
					}
					lines.Add(line);
				}
			}

			if (headlessTargetFile == null) {
				return "no headless target file specified";
			}

			string result;
			if (!asmstuff(stripcomments(lines.ToArray()), out result)) {
				return result;
			}

			result = cleandisasm(result.Split('\n'));

			using (var writer = new StreamWriter(headlessTargetFile)) {
				foreach (string line in result.Split('\n')) {
					writer.Write(line);
					writer.Write("\n");
				}
			}

			return null;
		}

		private static void parseDirective(string dir) {
			string[] p = dir.Split(new char[] {':'}, 2);
			p[1] = p[1].Trim();
			switch (p[0].Trim().ToUpper()) {
			case "HOOKADDR":
				hookaddr = p[1];
				break;
			case "JUMP":
				jump = p[1].ToLower() == "true";
				break;
			case "CORRECT_OFFSETS":
				correctoffsets = p[1].ToLower() == "true";
				break;
			case "COMMENTS":
				comments = p[1].ToLower() == "true";
				break;
			case "GCC":
				gcc = p[1];
				break;
			case "OBJDUMP":
				objdump = p[1];
				break;
			case "TARGETFILE":
				headlessTargetFile = p[1];
				break;
			}
		}

		private static string cleandisasm(string[] lines) {
			var sb = new StringBuilder();
			string[] instr = new string[8];
			int instrc;
			int instrs = 0;
			DATA.lvars.Clear();
			DATA.cache.Clear();
			SortedDictionary<int, A> stuff = new SortedDictionary<int, A>();
			sb.Append(":ENTRY").AppendLine();
			sb.Append("hex").AppendLine();
			int _ = 0;
			if (lines[_].Trim().Replace("\r", "").Length == 0) {
				do {
					_++;
				} while (!lines[_-1].StartsWith("00000000 <_main>"));
			}
			for (; _ < lines.Length; _++) {
				string _line = lines[_];
				try {
					string line = _line.Trim().Replace("\r", "");
					if (line.Length == 0) {
						continue;
					}
					if (line.EndsWith(":")) {
						if (comments) {
							sb.Append("// ").Append(line.Trim()).AppendLine();
						}
						continue;
					}
					instrc = 0;
					int c = 1;
					while (line[c - 1] != ':') c++;
					while (line[c] == ' ' || line[c] == '\t') c++;
					do {
						instr[instrc++] = new string(new char[] { line[c], line[c + 1] } );
						c += 3;
					} while (line[c] != ' ' && line [c] != '\t');
					while (line[c] == ' ' || line[c] == '\t') c++;
					string comment = line.Substring(c);
					
					int si = 0;
					if (instrc == 5 &&
							(comment.StartsWith("call") || comment.StartsWith("jmp") || comment.StartsWith("je ") || comment.StartsWith("jne ")) &&
							instraddr(instr) > 0x40000) {
						si = 1;
						sb.Append(instr[0]).AppendLine();
						instrs++;
						stuff.Add(instrs, new JMPCALL());
						int idx = comment.IndexOf(' ') + 1;
						comment = comment.Substring(0, idx) + "0x" + realaddr(instr);
						if (correctoffsets) {
							patchinstr(instr, 2 * instrs + 4);
						} else {
							patchinstr(instr, instrs);
						}
					}
					for (int i = 1; i <= instrc - 4; i++) {
						if (instr[i + 3] != "ee") {
							continue;
						}
						for (; si < i; si++) {
							sb.Append(instr[si]).Append(' ');
							instrs++;
						}
						stuff.Add(instrs, new DATA(instr[i + 2], (int.Parse(instr[i + 1], NumberStyles.HexNumber) << 8) | int.Parse(instr[i], NumberStyles.HexNumber)));
						sb.AppendLine();
						sb.Append("00 00 00 00 // DATA").Append(instr[i + 2]).AppendLine();
						instrs += 4;
						i += 3;
						si += 3;
						if (si == instrc - 1) {
							goto skiptocomment;
						}
					}
					for (; si < instrc; si++) {
						sb.Append(instr[si]).Append(' ');
						instrs++;
					}
					sb.AppendLine();
	skiptocomment:
					if (comments) {
						sb.Append("// ").Append(comment.Trim()).AppendLine();
					}
				} catch (Exception er) {
					string result = er.ToString() + "\r\n" + _line;
					MessageBox.Show(result);
					return result;
				}
			}
			sb.Append("end").AppendLine();
			var sb2 = new StringBuilder();
			sb2.Append(":HOOKER").AppendLine();
			sb2.Append("0AC6: 0@ = label @ENTRY offset").AppendLine();
			sb2.AppendLine();
			sb2.Append("0085: 1@ = 0@ // (int)").AppendLine();
			string[] hookadrs = new string[] {
				"",
				hookaddr.Substring(6, 2),
				hookaddr.Substring(4, 2),
				hookaddr.Substring(2, 2),
				hookaddr.Substring(0, 2),
			};
			sb2.Append("000E: 1@ -= 0x").Append(realaddr(hookadrs)).AppendLine();
			sb2.Append("0A8C: write_memory 0x").Append(i2hs(int.Parse(hookaddr, NumberStyles.HexNumber) - 1)).Append(" size 1 value 0xE9 vp 0").AppendLine();
			sb2.Append("0A8C: write_memory 0x").Append(hookaddr).Append(" size 4 value 1@ vp 0").AppendLine();
			sb2.AppendLine();
			sb2.Append("0AC6: 1@ = label @ENTRY offset").AppendLine();
			sb2.AppendLine();
			int offset = 0;
			foreach (KeyValuePair<int, A> s in stuff) {
				int diff = s.Key - offset;
				sb2.Append("000A: 1@ += ").Append(diff).AppendLine();
				offset += diff;
				s.Value.dostuff(sb2);
			}
			sb2.AppendLine();
			sb2.Append("0002: jump @NOMOREHOOKER").AppendLine().AppendLine();
			sb2.AppendLine();
			sb.Insert(0, sb2.ToString());
			return sb.ToString();
		}

		private void button1_Click(object sender, EventArgs e) {
			var lines = textBox1.Text.Split('\n');
			comments = checkBox1.Checked;
			correctoffsets = checkBox3.Checked;
			jump = checkBox2.Checked;
			hookaddr = textBox2.Text;
			textBox1.Text = cleandisasm(lines);
			Clipboard.SetText(textBox1.Text);
		}

		private static int instraddr(string[] instr) {
			return
				(int.Parse(instr[4], NumberStyles.HexNumber) << 24) |
				(int.Parse(instr[3], NumberStyles.HexNumber) << 16) |
				(int.Parse(instr[2], NumberStyles.HexNumber) <<  8) |
				(int.Parse(instr[1], NumberStyles.HexNumber)      ) |
				0;
		}

		private static string realaddr(string[] instr) {
			int addr = instraddr(instr);
			addr += 4;
			string s = "";
			s += ((addr >> 24) & 0xff).ToString("x2");
			s += ((addr >> 16) & 0xff).ToString("x2");
			s += ((addr >>  8) & 0xff).ToString("x2");
			s += ((addr      ) & 0xff).ToString("x2");
			return s;
		}

		private static string i2hs(int i) {
			string s = "";
			s += ((i >> 24) & 0xff).ToString("x2");
			s += ((i >> 16) & 0xff).ToString("x2");
			s += ((i >>  8) & 0xff).ToString("x2");
			s += ((i      ) & 0xff).ToString("x2");
			return s;
		}

		private static void patchinstr(string[] instr, int instrs) {
			int addr = instraddr(instr);
			addr -= instrs;
			instr[1] = ((addr      ) & 0xff).ToString("x2");
			instr[2] = ((addr >>  8) & 0xff).ToString("x2");
			instr[3] = ((addr >> 16) & 0xff).ToString("x2");
			instr[4] = ((addr >> 24) & 0xff).ToString("x2");
		}

		private static string b2h(byte[] b, int offset, int len)
		{
			StringBuilder sb = new StringBuilder();
			for(int i = offset, j = offset + len; i < j; i++) {
				sb.Append(b[i].ToString("X2"));
			}
			return sb.ToString();
		}

		private void button2_Click(object sender, EventArgs e) {
			textBox1.Text = stripcomments(textBox1.Text.Split('\n'));
			Clipboard.SetText(textBox1.Text);
		}

		private static string stripcomments(string[] lines) {
			var sb = new StringBuilder();
			foreach (var line in lines) {
				if (line.Trim().Length == 0) {
					continue;
				}
				string l = new Regex("_var(..)").Replace(line, "0xEE${1}0000");
				int i = l.IndexOf(';');
				if (i == -1) {
					sb.Append(l).AppendLine();
					continue;
				}
				string txt = l.Substring(0, i).Trim();
				if (txt.Length > 0) {
					sb.Append(txt).AppendLine();
				}
			}
			return sb.ToString();
		}

		public interface A {
			void dostuff(StringBuilder sb);
		}

		public class JMPCALL : A {
			void A.dostuff(StringBuilder sb) {
				sb.Append("0A8D: 2@ = read_memory 1@ size 4 vp 0").AppendLine();
				sb.Append("0062: 2@ -= 0@  // (int)").AppendLine();
				sb.Append("0A8C: write_memory 1@ size 4 value 2@ vp 0").AppendLine();
			}
		}

		public class DATA : A {
			public string n;
			public int offset;
			public static HashSet<int> lvars = new HashSet<int>();
			public static Dictionary<string, int> cache = new Dictionary<string, int>();
			public DATA(string n, int offset) {
				this.n = n;
				this.offset = offset;
			}
			void A.dostuff(StringBuilder sb) {
				int var = 2;
				if (cache.TryGetValue(n, out var)) {
					if (offset != 0) {
						goto dooffset;
					}
					goto skip;
				}
				for (int i = 3; i < 33; i++) {
					if (lvars.Contains(i)) {
						continue;
					}
					lvars.Add(i);
					cache.Add(n, i);
					var = i;
					break;
				}
				sb.Append("0AC6: ").Append(var).Append("@ = label @DATA").Append(n).Append(" offset").AppendLine();
dooffset:
				if (offset != 0) {
					sb.Append("0085: 2@ = ").Append(var).Append("@ // (int)").AppendLine();
					sb.Append("000A: 2@ += 0x").Append(offset).AppendLine();
					var = 2;
				}
skip:
				sb.Append("0A8C: write_memory 1@ size 4 value ").Append(var).Append("@ vp 0").AppendLine();
			}
		}

		private void textBox1_Click(object sender, EventArgs e) {
			textBox1.Text = "";
		}

		private void button4_Click(object sender, EventArgs e) {
			button2.PerformClick();
			gcc = txtgcc.Text;
			objdump = txtobjdump.Text;
			string res;
			if (!asmstuff(textBox1.Text, out res)) {
				textBox1.Text = res;
				return;
			}
			textBox1.Text = res;
			button1.PerformClick();
		}

		private static bool asmstuff(string code, out string result) {
			if (File.Exists(mydir + "/f.s")) File.Delete(mydir + "/f.s");
			if (File.Exists(mydir + "/f.o")) File.Delete(mydir + "/f.o");
			StreamWriter file = new StreamWriter(mydir + "/f.s");
			file.WriteLine(".intel_syntax noprefix\n_main:\n" + code);
			file.Close();
			StringBuilder o = new StringBuilder();
			bool res = false;
			if (exec(o, gcc, "-m32 -c f.s -o f.o")) {
				o.Clear();
				res = exec(o, objdump, "-M intel -d f.o");
			}
			result = o.ToString();
			if (File.Exists(mydir + "/f.s")) File.Delete(mydir + "/f.s");
			if (File.Exists(mydir + "/f.o")) File.Delete(mydir + "/f.o");
			return res;
		}

		public static string mydir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
		private static bool exec(StringBuilder o, string exe, string args)
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(exe, args);
			processStartInfo.UseShellExecute = false;
			//processStartInfo.ErrorDialog = false;
			processStartInfo.RedirectStandardError = true;
			//processStartInfo.RedirectStandardInput = true;
			processStartInfo.RedirectStandardOutput = true;
			//processStartInfo.CreateNoWindow = true;
			processStartInfo.WorkingDirectory = mydir;
			Process process = new Process();
			process.StartInfo = processStartInfo;
			try
			{
				process.Start();
			}
			catch (Exception ex)
			{
				Console.WriteLine(exe + " could not commence.");
				o.Append(ex.Message);
				return false;
			}

			// meh, works
			o.AppendLine(process.StandardOutput.ReadToEnd());
			o.AppendLine(process.StandardError.ReadToEnd());

			process.WaitForExit();

			return process.ExitCode == 0;
		}

		private void button3_Click(object sender, EventArgs e) {
			textBox1.Text = Clipboard.GetText();
			button4.PerformClick();
		}
	}
}