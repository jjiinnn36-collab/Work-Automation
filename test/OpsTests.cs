using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    /// <summary>운영 부품 시험: 기록 파일 순환, 날짜별 백업·정리, 경고 문구 (ADR-0008).</summary>
    static class OpsTests
    {
        static int passed = 0, failed = 0;

        static void Check(string name, object actual, object expected)
        {
            string a = Convert.ToString(actual, CultureInfo.InvariantCulture);
            string e = Convert.ToString(expected, CultureInfo.InvariantCulture);
            if (a == e) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "  기대=" + e + " 실제=" + a); }
        }

        static void CheckTrue(string name, bool c)
        {
            if (c) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        static string 임시폴더(string tag)
        {
            string d = Path.Combine(Path.GetTempPath(), "pa_ops_" + tag + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        static void 치우기(string d) { try { Directory.Delete(d, true); } catch { } }

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                기록순환();
                날짜별백업();
                백업위치설정();
                경고문구();
                납부서판독도구();
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("  FAIL  예외: " + ex);
            }
            Console.WriteLine();
            Console.WriteLine(new string('=', 50));
            Console.WriteLine(string.Format("  통과 {0}건 / 실패 {1}건", passed, failed));
            Console.WriteLine(new string('=', 50));
            return failed == 0 ? 0 : 1;
        }

        static void 기록순환()
        {
            Console.WriteLine("\n[OPS-1] 실행 기록은 크기를 넘으면 밀어내고 N개만 남긴다");
            string dir = 임시폴더("log");
            try
            {
                string log = Path.Combine(dir, "sub", "run.log");
                DateTime t = new DateTime(2026, 9, 16, 9, 0, 0);
                LogFile.Append(log, "첫 줄", 200, 2, t);
                CheckTrue("폴더가 없어도 만든다", File.Exists(log));
                Check("시각과 내용", File.ReadAllText(log, Encoding.UTF8).TrimStart('\uFEFF').Trim(), "2026-09-16 09:00:00\t첫 줄");

                for (int i = 0; i < 40; i++) LogFile.Append(log, "줄 " + i + " " + new string('가', 10), 200, 2, t);
                CheckTrue("run.1.log 생김", File.Exists(Path.Combine(dir, "sub", "run.1.log")));
                CheckTrue("run.2.log 생김", File.Exists(Path.Combine(dir, "sub", "run.2.log")));
                CheckTrue("보관 수를 넘는 run.3.log 는 없음", !File.Exists(Path.Combine(dir, "sub", "run.3.log")));
                CheckTrue("현재 파일은 한도 근처", new FileInfo(log).Length < 400);
                CheckTrue("마지막 줄은 현재 파일에", File.ReadAllText(log, Encoding.UTF8).Contains("줄 39"));

                // 쓸 수 없는 경로여도 예외를 내지 않는다
                LogFile.Append(dir + "\\<bad>|\\x.log", "무시", 100, 1, t);
                CheckTrue("잘못된 경로에도 멈추지 않음", true);
            }
            finally { 치우기(dir); }
        }

        static void 날짜별백업()
        {
            Console.WriteLine("\n[OPS-2] 하루 한 번 사본, 최근 N개만 보관, 수동 사본은 지우지 않음");
            string dir = 임시폴더("bak");
            try
            {
                string folder = Path.Combine(dir, "backups");
                using (Store s = Store.Open(Path.Combine(dir, "t.db")))
                {
                    var it = new PaymentItem { Id = "a", 기관 = "기관", 비용명 = "비용", 진행흐름 = Flow.납부만, 월 = 3, 일 = 10, 알림영업일 = 3 };
                    s.UpsertItem(it);

                    string first = Backups.일일백업(s, folder, new DateTime(2026, 9, 1), 3);
                    CheckTrue("첫 사본 생성", first != null && File.Exists(first));
                    Check("이름에 날짜", Path.GetFileName(first), "납부알림-2026-09-01.db");
                    CheckTrue("같은 날 두 번째는 건너뜀", Backups.일일백업(s, folder, new DateTime(2026, 9, 1), 3) == null);
                    CheckTrue("임시 파일이 남지 않음", Directory.GetFiles(folder, "*.tmp").Length == 0);

                    using (Store copy = Store.Open(first))
                        Check("사본에 자료가 온전", copy.LoadMaster().Count, 1);

                    string manual = Backups.지금백업(s, folder, new DateTime(2026, 9, 2, 13, 5, 9));
                    Check("수동 사본 이름", Path.GetFileName(manual), "납부알림-수동-20260902-130509.db");

                    for (int d = 2; d <= 6; d++) Backups.일일백업(s, folder, new DateTime(2026, 9, d), 3);
                    var names = new List<string>();
                    foreach (string f in Directory.GetFiles(folder, "납부알림-2026-*.db")) names.Add(Path.GetFileName(f));
                    names.Sort();
                    Check("날짜별은 최근 3개만", string.Join(",", names.ToArray()),
                        "납부알림-2026-09-04.db,납부알림-2026-09-05.db,납부알림-2026-09-06.db");
                    CheckTrue("수동 사본은 남음", File.Exists(manual));

                    List<Backups.사본> list = Backups.목록(folder);
                    Check("목록 수 (날짜 3 + 수동 1)", list.Count, 4);
                    CheckTrue("크기 기록", list[0].크기 > 0);
                    CheckTrue("없는 폴더 목록은 빈 목록", Backups.목록(Path.Combine(dir, "none")).Count == 0);
                }
            }
            finally { 치우기(dir); }
        }

        static void 백업위치설정()
        {
            Console.WriteLine("\n[OPS-3] backup-folder.txt 로 백업 위치를 바꾼다");
            string dir = 임시폴더("bakcfg");
            try
            {
                string data = Path.Combine(dir, "data");
                Check("설정이 없으면 자료폴더\\backups", Backups.폴더(dir, data), Path.Combine(data, "backups"));
                File.WriteAllText(Path.Combine(dir, Backups.설정파일), "# 주석\r\n\"D:\\안전한곳\\백업\"\r\n", new UTF8Encoding(true));
                Check("절대 경로", Backups.폴더(dir, data), "D:\\안전한곳\\백업");
                File.WriteAllText(Path.Combine(dir, Backups.설정파일), "bk\r\n", new UTF8Encoding(false));
                Check("상대 경로는 실행 폴더 기준", Backups.폴더(dir, data), Path.Combine(dir, "bk"));
            }
            finally { 치우기(dir); }
        }

        static void 경고문구()
        {
            Console.WriteLine("\n[OPS-4] 공휴일 자료 경고");
            var c = new Holidays.Cache();
            c.Years.Add(2025); c.Years.Add(2026);
            c.Updated = new DateTime(2026, 9, 1);
            var years = new int[] { 2025, 2026, 2027 };
            List<string> w = Warnings.Build(c, years, new DateTime(2026, 9, 16));
            Check("빠진 연도 경고 1건", w.Count, 1);
            CheckTrue("2027 을 짚음", w[0].StartsWith("2027년 공휴일 자료가 없습니다"));

            c.Years.Add(2027);
            Check("다 있고 최근이면 경고 없음", Warnings.Build(c, years, new DateTime(2026, 9, 16)).Count, 0);
            w = Warnings.Build(c, years, new DateTime(2026, 12, 15));
            Check("90일 넘으면 경고", w.Count, 1);
            CheckTrue("경과일 표시", w[0].Contains("105일"));
            Check("AppInfo 판 형식", System.Text.RegularExpressions.Regex.IsMatch(AppInfo.버전, @"^\d+\.\d+\.\d+$"), true);
        }

        /// <summary>tools\import-notice.ps1 -TextFile -Result. Word 없이 판독·검산 규칙만 시험한다.</summary>
        static Dictionary<string, string> 판독(string text, out int code)
        {
            string dir = 임시폴더("vat");
            try
            {
                string tf = Path.Combine(dir, "extracted.txt");
                File.WriteAllText(tf, text, new UTF8Encoding(false));
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.GetFullPath(@"tools\import-notice.ps1") +
                    "\" -TextFile \"" + tf + "\" -Result");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.CreateNoWindow = true;
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    code = p.ExitCode;
                    var map = new Dictionary<string, string>();
                    foreach (string raw in output.Split('\n'))
                    {
                        string line = raw.TrimEnd('\r');
                        int tab = line.IndexOf('\t');
                        if (tab > 0) map[line.Substring(0, tab)] = line.Substring(tab + 1);
                    }
                    return map;
                }
            }
            finally { 치우기(dir); }
        }

        static void 납부서판독도구()
        {
            Console.WriteLine("\n[OPS-5] 부가세 납부서 판독 도구 — 결과 줄 모드와 검산 규칙 (AC-W17, ADR-0012)");
            // 실제 납부서에서 Word 로 뽑으면 칸 이름 한 줄, 값 한 줄씩 나온다. 같은 면이 여러 번 반복된다.
            string 면 =
                "국세징수법 시행규칙 별지 납부서\n납부기한 2026년 10월 26일\n" +
                "부가가치세\n12,000,000\n교육/방위세\n0\n농어촌특별세\n0\n가산금\n34,500\n계\n12,034,500\n";
            int code;
            Dictionary<string, string> r = 판독(면 + 면, out code);
            Check("정상 판독 종료 코드 0", code, 0);
            Check("ok", r.ContainsKey("ok") ? r["ok"] : "(없음)", "true");
            Check("납부기한", r.ContainsKey("due") ? r["due"] : "", "2026-10-26");
            Check("세목 합계", r.ContainsKey("sum") ? r["sum"] : "", "12034500");
            Check("문서상 계", r.ContainsKey("total") ? r["total"] : "", "12034500");
            Check("가산금", r.ContainsKey("surcharge") ? r["surcharge"] : "", "34500");
            CheckTrue("안내 문구는 섞이지 않음", !r.ContainsKey("  파일:"));

            string 틀린면 = 면.Replace("계\n12,034,500", "계\n12,034,501");
            r = 판독(틀린면, out code);
            Check("세목 합계 ≠ 계 → 종료 코드 6", code, 6);
            Check("반영 거절", r.ContainsKey("ok") ? r["ok"] : "", "false");
            CheckTrue("이유 문구", r.ContainsKey("message") && r["message"].Contains("일치하지"));

            r = 판독(면 + 틀린면, out code);
            Check("면마다 계가 다르면 코드 5", code, 5);

            r = 판독("금융투자협회 회비 안내\n계\n5,000,000\n", out code);
            Check("부가세 납부서가 아니면 코드 3", code, 3);
            CheckTrue("직접 입력 안내", r.ContainsKey("message") && r["message"].Contains("직접 입력"));

            r = 판독("국세징수법 부가가치세\n계\n100\n", out code);
            Check("납부기한이 없으면 코드 4", code, 4);
        }
    }
}
