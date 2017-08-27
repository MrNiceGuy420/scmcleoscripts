﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace asm {
	public partial class Form1 : Form {

		[STAThread]
		static void Main() {
			Application.SetCompatibleTextRenderingDefault(true);
			Application.Run(new Form1());
		}

		public Form1() {
			InitializeComponent();
		}

		private void button1_Click(object sender, EventArgs e) {
			var lines = textBox1.Text.Split('\n');
			var sb = new StringBuilder();
			string[] instr = new string[8];
			int instrc;
			int instrs = 0;
			DATA.lvars.Clear();
			DATA.cache.Clear();
			SortedDictionary<int, A> stuff = new SortedDictionary<int, A>();
			sb.Append(":ENTRY").AppendLine();
			sb.Append("hex").AppendLine();
			foreach (var line in lines) {
				try {
					if (line.Trim().EndsWith(":")) {
						if (checkBox1.Checked) {
							sb.Append("// ").Append(line.Trim()).AppendLine();
						}
						continue;
					}
					instrc = 0;
					int c = 1;
					while (line[c - 1] != ':') c++;
					while (line[c] == ' ') c++;
					do {
						instr[instrc++] = new string(new char[] { line[c], line[c + 1] } );
						c += 3;
					} while (line[c] != ' ');
					while (line[c] == ' ') c++;
					string comment = line.Substring(c);
					
					int si = 0;
					if (instrc == 5 &&
							(comment.StartsWith("call") || comment.StartsWith("jmp") || comment.StartsWith("je ") || comment.StartsWith("jne "))) {
						si = 1;
						sb.Append(instr[0]).AppendLine();
						instrs++;
						stuff.Add(instrs, new JMPCALL());
						int idx = comment.IndexOf(' ') + 1;
						comment = comment.Substring(0, idx) + "0x" + realaddr(instr);
						patchinstr(instr, instrs);
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
					if (checkBox1.Checked) {
						sb.Append("// ").Append(comment.Trim()).AppendLine();
					}
				} catch (Exception er) {
					MessageBox.Show(er.ToString() + "\r\n" + line);
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
				textBox2.Text.Substring(6, 2),
				textBox2.Text.Substring(4, 2),
				textBox2.Text.Substring(2, 2),
				textBox2.Text.Substring(0, 2),
			};
			sb2.Append("000E: 1@ -= 0x").Append(realaddr(hookadrs)).AppendLine();
			sb2.Append("0A8C: write_memory 0x").Append(i2hs(int.Parse(textBox2.Text, NumberStyles.HexNumber) - 1)).Append(" size 1 value 0xE9 vp 0").AppendLine();
			sb2.Append("0A8C: write_memory 0x").Append(textBox2.Text).Append(" size 4 value 1@ vp 0").AppendLine();
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
			textBox1.Text = sb.ToString();
			Clipboard.SetText(textBox1.Text);
		}

		private string realaddr(string[] instr) {
			int addr =
				(int.Parse(instr[4], NumberStyles.HexNumber) << 24) |
				(int.Parse(instr[3], NumberStyles.HexNumber) << 16) |
				(int.Parse(instr[2], NumberStyles.HexNumber) <<  8) |
				(int.Parse(instr[1], NumberStyles.HexNumber)      ) |
				0;
			addr += 4;
			string s = "";
			s += ((addr >> 24) & 0xff).ToString("x2");
			s += ((addr >> 16) & 0xff).ToString("x2");
			s += ((addr >>  8) & 0xff).ToString("x2");
			s += ((addr      ) & 0xff).ToString("x2");
			return s;
		}

		private string i2hs(int i) {
			string s = "";
			s += ((i >> 24) & 0xff).ToString("x2");
			s += ((i >> 16) & 0xff).ToString("x2");
			s += ((i >>  8) & 0xff).ToString("x2");
			s += ((i      ) & 0xff).ToString("x2");
			return s;
		}

		private void patchinstr(string[] instr, int instrs) {
			int addr =
				(int.Parse(instr[4], NumberStyles.HexNumber) << 24) |
				(int.Parse(instr[3], NumberStyles.HexNumber) << 16) |
				(int.Parse(instr[2], NumberStyles.HexNumber) <<  8) |
				(int.Parse(instr[1], NumberStyles.HexNumber)      ) |
				0;
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
			var lines = textBox1.Text.Split('\n');
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
			textBox1.Text = sb.ToString();
			Clipboard.SetText(textBox1.Text);
		}

		private void button3_Click(object sender, EventArgs e) {
			textBox1.Text = "";
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
	}
}
