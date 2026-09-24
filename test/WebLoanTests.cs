using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace PaymentAlert.Tests
{
    /// <summary>
    /// [WEB-19] 차입 스케줄 가져오기 (ADR-0023). 값은 모두 가짜다.
    /// 다른 시험이 센 건수를 흔들지 않도록 맨 마지막에 돈다.
    /// </summary>
    static partial class WebTests
    {
        static readonly string[] 차입머리 = {
            "No.", "기준일자", "순번", "전표일자", "자금요청일", "현금흐름구분", "액면금액", "상환금액",
            "액면이자금액", "액면이자율", "전표번호", "잔액관리번호", "지급전표번호", "전표적요", "거래처명",
            "거래처 지급계좌번호", "구ERP여부", "마감여부" };

        static string[] 차입행(string date, string kind, string face, string interest, string org, string rate)
        {
            return new[] { "1", date, "1", date, date, kind, face, "0", interest, rate, "", "", "", "", org, "", "", "Y" };
        }

        /// <summary>차입처A: 15억·4.2%·2026-09-04. 분기 15,750,000. 1회차 조각 금액을 바꿔 '금액 다름' 을 만든다.</summary>
        static List<string[]> 차입A(string 첫조각)
        {
            var rows = new List<string[]> { 차입머리, 차입행("2026-09-04", "신규", "1500000000", "0", "차입처A", "4.2") };
            string[,] x = {
                { "2026-09-30", "4660274" }, { "2026-10-31", "5350685" }, { "2026-11-30", "5178082" }, { "2026-12-04", 첫조각 },
                { "2026-12-31", "4832877" }, { "2027-01-31", "5350685" }, { "2027-02-28", "4832877" }, { "2027-03-04", "733561" },
                { "2027-03-31", "4832877" }, { "2027-04-30", "5178082" }, { "2027-05-31", "5350685" }, { "2027-06-04", "388356" },
                { "2027-06-30", "4660274" }, { "2027-07-31", "5350685" }, { "2027-08-31", "5350685" }, { "2027-09-03", "388356" } };
            for (int i = 0; i < x.GetLength(0); i++) rows.Add(차입행(x[i, 0], "이자지급", "0", x[i, 1], "차입처A", "4.2"));
            rows.Add(차입행("2027-09-03", "상환", "0", "0", "차입처A", "4.2"));
            return rows;
        }

        /// <summary>차입처B: 5억·5%·2026-10-16. 1회차 지급일 2027-01-16(토) — ERP 날짜 그대로 두면 월요일로 밀린다.</summary>
        static List<string[]> 차입B()
        {
            var rows = new List<string[]> { 차입머리, 차입행("2026-10-16", "신규", "500000000", "0", "차입처B", "5") };
            rows.Add(차입행("2026-10-31", "이자지급", "0", "1027397", "차입처B", "5"));
            rows.Add(차입행("2026-11-30", "이자지급", "0", "2054795", "차입처B", "5"));
            rows.Add(차입행("2026-12-31", "이자지급", "0", "2123288", "차입처B", "5"));
            rows.Add(차입행("2027-01-16", "이자지급", "0", "1044520", "차입처B", "5"));
            return rows;
        }

        static string 시트XML(List<string[]> rows)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append("<row r=\"").Append(r + 1).Append("\">");
                for (int c = 0; c < rows[r].Length; c++)
                {
                    string v = rows[r][c];
                    if (string.IsNullOrEmpty(v)) continue;
                    string refName = ((char)('A' + c)).ToString() + (r + 1);
                    long n;
                    if (long.TryParse(v, out n))
                        sb.Append("<c r=\"").Append(refName).Append("\"><v>").Append(v).Append("</v></c>");
                    else
                        sb.Append("<c r=\"").Append(refName).Append("\" t=\"inlineStr\"><is><t>").Append(SecurityElement.Escape(v)).Append("</t></is></c>");
                }
                sb.Append("</row>");
            }
            return sb.Append("</sheetData></worksheet>").ToString();
        }

        /// <summary>시트 이름 → 행. 행이 null 이면 빈 시트.</summary>
        static byte[] 엑셀(params KeyValuePair<string, List<string[]>>[] sheets)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var wb = new StringBuilder("<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                    var rels = new StringBuilder("<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                    for (int i = 0; i < sheets.Length; i++)
                    {
                        wb.Append("<sheet name=\"").Append(SecurityElement.Escape(sheets[i].Key)).Append("\" sheetId=\"").Append(i + 1)
                          .Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
                        rels.Append("<Relationship Id=\"rId").Append(i + 1).Append("\" Type=\"ws\" Target=\"worksheets/sheet").Append(i + 1).Append(".xml\"/>");
                        Zip(z, "xl/worksheets/sheet" + (i + 1) + ".xml", 시트XML(sheets[i].Value ?? new List<string[]>()));
                    }
                    Zip(z, "xl/workbook.xml", wb.Append("</sheets></workbook>").ToString());
                    Zip(z, "xl/_rels/workbook.xml.rels", rels.Append("</Relationships>").ToString());
                }
                return ms.ToArray();
            }
        }

        static void Zip(ZipArchive z, string name, string text)
        {
            using (var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false))) w.Write(text);
        }

        static KeyValuePair<string, List<string[]>> 시트(string name, List<string[]> rows)
        {
            return new KeyValuePair<string, List<string[]>>(name, rows);
        }

        static string Csv(List<string[]> rows)
        {
            var sb = new StringBuilder();
            foreach (string[] r in rows)
            {
                var cells = new List<string>();
                foreach (string c in r) cells.Add(c.IndexOf(',') >= 0 ? "\"" + c + "\"" : c);
                sb.Append(string.Join(",", cells.ToArray())).Append("\r\n");
            }
            return sb.ToString();
        }

        static Res 올리기(string query, byte[] body, string fileName)
        {
            var h = 표시();
            if (fileName != null) h["X-File-Name"] = Uri.EscapeDataString(fileName);
            return Send("POST", "/api/import/loans" + query, body, "application/octet-stream", h);
        }

        static void 차입스케줄가져오기()
        {
            Console.WriteLine("\n[WEB-19] 차입 스케줄 가져오기 — 미리보기·저장·다시 올리기 (ADR-0023)");
            byte[] xlsx = 엑셀(시트("차입처A", 차입A("560959")), 시트("빈 시트", null),
                              시트("메모", new List<string[]> { new[] { "참고", "내용" } }));

            Res r = Send("POST", "/api/import/loans?mode=preview", xlsx, "application/octet-stream",
                new Dictionary<string, string> { { "X-File-Name", "a.xlsx" } });
            Check("표시 머리글 없으면 403", r.Status, 403);

            r = 올리기("?mode=preview", xlsx, "차입 스케줄.xlsx");
            Check("미리보기 200", r.Status, 200);
            Has("파일 이름", r.Text, "\"file\":\"차입 스케줄.xlsx\"");
            Has("거래처", r.Text, "\"org\":\"차입처A\"");
            Has("차입일", r.Text, "\"start\":\"2026-09-04\"");
            Has("분기 예상", r.Text, "\"quarterExpected\":15750000");
            Has("새 회차 4", r.Text, "\"newCount\":4");
            Has("1회차 지급일", r.Text, "\"scheduled\":\"2026-12-04\"");
            Has("4회차 = ERP 날짜 9/3", r.Text, "\"scheduled\":\"2027-09-03\"");
            Has("분기 금액", r.Text, "\"amount\":15750000");
            Has("근거 행", r.Text, "{\"date\":\"2026-12-04\",\"amount\":560959}");
            Has("빈 시트 건너뜀", r.Text, "{\"sheet\":\"빈 시트\",\"reason\":\"빈 시트\"}");
            Has("양식 아닌 시트 건너뜀", r.Text, "\"sheet\":\"메모\",\"reason\":\"차입 스케줄 양식이 아님");
            Lacks("미리보기는 저장 안 함", Get("/api/items").Text, "차입처A");
            Lacks("미리보기 결과에 계좌 열 값 없음", r.Text, "지급계좌");

            // 차입명을 안 보내면 시트명을 차입명으로 쓴다 (사용자 요청 2026-09-22).
            Has("차입명 자리에 시트명", r.Text, "\"name\":\"차입처A\"");
            Check("차입명이 너무 길면 400", 올리기("?mode=save&nm=" + E("차입처A|" + new string('가', 61) + "|"), xlsx, "차입 스케줄.xlsx").Status, 400);

            r = 올리기("?mode=save&nm=" + E("차입처A|가짜 차입|가짜PF"), xlsx, "차입 스케줄.xlsx");
            Check("저장 200", r.Status, 200);
            Has("4회차 들어감", r.Text, "\"added\":4");
            Has("있던 회차 0", r.Text, "\"existing\":0");
            Has("오늘(9/17) 알릴 건 아님", r.Text, "\"popup\":false");
            string group = Regex.Match(r.Text, "\"group\":\"(item-\\d{4})\"").Groups[1].Value;
            CheckTrue("묶음 번호 " + group, group.Length > 0);

            string items = Get("/api/items").Text;
            Has("항목 목록에 차입처A", items, "\"id\":\"" + group + "-202612\"");
            Has("다른 비용과 같은 '납부만' 흐름", items, "\"name\":\"차입금 이자\",\"flow\":\"납부만\"");
            Lacks("이름에 회차 번호 없음", items, "회차\"");
            Has("차입처 자리에 약칭", items, "\"id\":\"" + group + "-202612\",\"org\":\"가짜PF\"");
            Has("고지서수령 없이 지급액 확인 → 전표발행 → 납부", items, "\"stages\":[\"지급액 확인\",\"전표발행\",\"납부\"],\"actions\":[\"\",\"전표발행\",\"납부완료\"]");
            Has("유효연도", items, "\"startYear\":2026,\"endYear\":2026");
            Has("비고는 이자 기간만", items, "\"memo\":\"이자기간 2026-09-04~2026-12-03\"");
            Has("차입 회차 표시", items, "\"loan\":true");
            Has("올해 회차는 올해 금액", items, "\"endYear\":2026,\"thisYearAmount\":15750000");
            Has("올해 기한이 없는 회차는 올해 금액 '미확인' 아님", items, "\"endYear\":2027,\"thisYearAmount\":null,\"thisYearEntered\":false,\"thisYearUnknown\":false");

            string y26 = Get("/api/year?y=2026").Text;
            Has("2026 연간에 1회차", y26, "\"id\":\"" + group + "-202612\"");
            Lacks("2026 연간에 2027 회차 없음", y26, group + "-202703");
            string y27 = Get("/api/year?y=2027").Text;
            Has("2027 연간에 2~4회차", y27, group + "-202709");
            Lacks("2027 연간에 1회차 없음", y27, group + "-202612");
            Lacks("2028 연간에는 없음", Get("/api/year?y=2028").Text, group);
            Has("금액이 연도별 금액으로", y27, "\"amountText\":\"15,750,000원\"");

            Res g = Get("/api/group?y=2027&group=" + group);
            Check("묶음 2027 200", g.Status, 200);
            Has("2027 회차 3개", g.Text, "\"count\":3");
            Has("2027 합계", g.Text, "\"total\":47250000");
            Check("그 해 회차가 없으면 404", Get("/api/group?y=2025&group=" + group).Status, 404);
            Check("유효연도 밖 진행은 400", Post("/api/advance", "y=2027&id=" + group + "-202612&stage=0").Status, 400);
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                CheckTrue("거절된 진행은 기록 안 남김", !db.LoadStatus().ContainsKey("2027\t" + group + "-202612"));
                Check("거절된 진행은 변경 기록도 없음", db.LoadEvents(2027, group + "-202612").Count, 0);
            }
            Check("유효연도 밖 금액은 400", Post("/api/amount", "y=2028&id=" + group + "-202612&amount=1").Status, 400);

            // 1회차를 진행하고, 금액이 바뀐 파일을 다시 올린다.
            Check("1회차 전표 발행", Post("/api/advance", "y=2026&id=" + group + "-202612&stage=0").Status, 200);
            byte[] changed = 엑셀(시트("차입처A", 차입A("560960")));
            r = 올리기("?mode=preview", changed, "차입 스케줄(2).xlsx");
            Has("알던 차입건", r.Text, "\"known\":true");
            Has("알던 차입건의 이름", r.Text, "\"name\":\"가짜 차입\",\"short\":\"가짜PF\"");
            Has("이미 있음 3", r.Text, "\"existingCount\":3");
            Has("금액 다름 1", r.Text, "\"diffCount\":1");
            Has("다른 회차의 기존 금액", r.Text, "\"state\":\"diff\",\"existingAmount\":15750000");
            r = 올리기("?mode=save", changed, "차입 스케줄(2).xlsx");
            Has("다시 저장해도 새 회차 0", r.Text, "\"added\":0");
            Has("있던 회차 4", r.Text, "\"existing\":4");
            Has("같은 묶음", r.Text, "\"group\":\"" + group + "\"");
            Check("다시 올릴 때는 이름을 안 보내도 됨", r.Status, 200);
            Check("진행은 그대로", 상태(group + "-202612").단계, 1);
            Has("금액도 그대로", Get("/api/group?y=2026&group=" + group).Text, "\"total\":15750000");

            // 항목 수정 화면이 유효연도를 보내지 않아도 기간이 풀리지 않는다.
            r = Post("/api/item", "mode=edit&id=" + group + "-202612&org=" + E("차입처A") + "&name=" + E("차입금 이자 1회차") +
                "&flow=" + E("납부만") + "&month=12&day=4&lead=5&rule=" + E("변동"));
            Check("회차 고치기 200", r.Status, 200);
            Has("유효연도 유지", r.Text, "\"startYear\":2026,\"endYear\":2026");
            Has("옛 이름에 회차가 있어도 화면에는 번호 없이", r.Text, "\"name\":\"차입금 이자\",");
            r = Post("/api/item", "mode=edit&id=" + group + "-202612&org=" + E("차입처A") + "&name=x&flow=" + E("납부만") +
                "&month=12&day=4&startYear=2027&endYear=2026");
            Check("종료가 시작보다 빠르면 400", r.Status, 400);

            // CSV: 휴일로 들어온 ERP 날짜는 다음 영업일, only 로 고른 시트만 저장.
            byte[] csv = Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes(Csv(차입B())));
            r = 올리기("?mode=preview", csv, "차입처B.csv");
            Check("CSV 미리보기 200", r.Status, 200);
            Has("CSV 시트 이름 = 파일 이름", r.Text, "\"sheet\":\"차입처B\"");
            Has("ERP 날짜가 토요일이면 월요일", r.Text, "\"scheduled\":\"2027-01-16\",\"payDue\":\"2027-01-18\",\"shifted\":true");
            Has("B 분기 합", r.Text, "\"amount\":6250000");

            byte[] two = 엑셀(시트("차입처A", 차입A("560959")), 시트("차입처B", 차입B()));
            r = 올리기("?mode=save&only=" + E("차입처B") + "&nm=" + E("차입처B|가짜B차입|"), two, "두 건.xlsx");
            Has("고른 시트만: B 1회차", r.Text, "\"added\":1");
            Has("A 는 고르지 않음", r.Text, "\"sheet\":\"차입처A\"");
            Has("선택 표시", r.Text, "\"selected\":false");
            Has("B 저장됨 — 약칭이 없으면 차입명", Get("/api/items").Text, "\"org\":\"가짜B차입\",\"name\":\"차입금 이자\"");

            Console.WriteLine("\n[WEB-19b] 읽을 수 없는 파일은 이유와 함께 400");
            byte[] ole = new byte[512];
            ole[0] = 0xD0; ole[1] = 0xCF; ole[2] = 0x11; ole[3] = 0xE0;
            r = 올리기("?mode=preview", ole, "보안문서.xlsx");
            Check("암호·문서보안 파일 400", r.Status, 400);
            Has("안내 문구", r.Text, "문서보안(DRM)");
            Check("파일 이름 없으면 400", 올리기("?mode=preview", xlsx, null).Status, 400);
            Check("빈 본문 400", 올리기("?mode=preview", new byte[0], "a.xlsx").Status, 400);
            Check("모르는 mode 400", 올리기("?mode=go", xlsx, "a.xlsx").Status, 400);
            Check("깨진 엑셀 400", 올리기("?mode=preview", new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 }, "a.xlsx").Status, 400);

            var noNew = 차입A("560959");
            noNew.RemoveAt(1);
            r = 올리기("?mode=preview", 엑셀(시트("신규없음", noNew)), "b.xlsx");
            Check("양식은 맞지만 신규 행 없음 → 200 + 시트 오류", r.Status, 200);
            Has("시트 오류 문구", r.Text, "\"sheet\":\"신규없음\",\"ok\":false,\"error\":\"'신규' 행");
            r = 올리기("?mode=save", 엑셀(시트("신규없음", noNew)), "b.xlsx");
            Has("오류 시트는 저장 안 함", r.Text, "\"added\":0");

            차입원본증빙(group, xlsx);
            연장스케줄업로드(group);
            차입건삭제(group);
        }

        /// <summary>
        /// 차입 원본 스케줄 증빙 (사용자 결정 2026-09-23).
        /// 사업건마다 CSV 한 부를 차입건에 붙이고, 회차 어디서 열어도 보이며, 차입건을 지우면 함께 지워진다.
        /// </summary>
        static void 차입원본증빙(string group, byte[] xlsx)
        {
            string 회차 = group + "-202612";
            Res a = Get("/api/attachments?y=2026&id=" + 회차);
            Check("증빙 목록 200", a.Status, 200);
            Has("이 차입건이 달려 있음", a.Text, "\"loanGroup\":\"" + group + "\"");
            Has("원본 스케줄 한 부", a.Text, "\"name\":\"가짜 차입.csv\"");

            // 바로 앞과 내용이 같으면 새로 쌓지 않는다 (같은 파일을 여러 번 올려도 한 부).
            올리기("?mode=save", xlsx, "차입 스케줄.xlsx");
            int 먼저 = 원본부수(group);
            올리기("?mode=save", xlsx, "차입 스케줄.xlsx");
            Check("같은 내용은 새로 안 쌓임", 원본부수(group), 먼저);
            CheckTrue("원본이 한 부 이상 있다", 먼저 >= 1);

            // 다른 회차에서 열어도 같은 원본이 보인다.
            Has("2027 회차에서도 같은 원본", Get("/api/attachments?y=2027&id=" + group + "-202709").Text, "\"name\":\"가짜 차입.csv\"");
            // 차입이 아닌 항목에는 붙지 않는다.
            Has("일반 항목은 차입건 없음", Get("/api/attachments?y=2026&id=kofia").Text, "\"loanGroup\":null");

            Res s = Get("/api/loan/summary?group=" + group);
            Check("요약 200", s.Status, 200);
            Has("회차 4건", s.Text, "\"items\":4");
            Check("없는 차입건 요약은 404", Get("/api/loan/summary?group=item-9999").Status, 404);
        }

        /// <summary>이 차입건에 쌓인 원본 스케줄 부수.</summary>
        static int 원본부수(string group)
        {
            string t = Get("/api/loan/summary?group=" + group).Text;
            const string key = "\"docs\":";
            int i = t.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return -1;
            int j = i + key.Length, n = 0;
            while (j < t.Length && t[j] >= '0' && t[j] <= '9') { n = n * 10 + (t[j] - '0'); j++; }
            return n;
        }

        /// <summary>차입건 삭제 (ㄷ안): 기본은 금액·진행을 남기고 차입건·회차·원본 파일만 지운다.</summary>
        static void 차입건삭제(string group)
        {
            string 회차 = group + "-202612";
            Res d = Post("/api/loan/delete", "group=" + group);
            Check("삭제 200", d.Status, 200);
            Has("기록은 남김", d.Text, "\"purged\":false");
            Has("원본 파일도 지움", d.Text, "\"docs\":4");
            Lacks("차입 목록에서 빠짐", Get("/api/loans").Text, "\"group\":\"" + group + "\"");
            Lacks("항목에서도 빠짐", Get("/api/items").Text, "\"id\":\"" + 회차 + "\"");
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                CheckTrue("금액은 남아 있다 (다시 가져오면 이어지게)", db.LoadAmounts().ContainsKey("2026\t" + 회차));
                CheckTrue("원본 목록도 비었다", db.차입원본목록(group).Count == 0);
                CheckTrue("무엇을 지웠는지는 기록에 남는다", db.LoadEvents(2026, group).Count > 0);
            }
            Check("이미 지운 차입건은 404", Post("/api/loan/delete", "group=" + group).Status, 404);
        }

        /// <summary>차입처A 1차 연장: 2027-09-03 시작, 4.5%, 분기 16,875,000. 두 분기만.</summary>
        static List<string[]> 연장A(string org)
        {
            var rows = new List<string[]> { 차입머리, 차입행("2027-09-03", "신규", "1500000000", "0", org, "4.5") };
            string[,] x = {
                { "2027-09-30", "4000000" }, { "2027-10-31", "5000000" }, { "2027-11-30", "5000000" }, { "2027-12-03", "2875000" },
                { "2027-12-31", "5000000" }, { "2028-01-31", "5000000" }, { "2028-02-29", "4000000" }, { "2028-03-03", "2875000" } };
            for (int i = 0; i < x.GetLength(0); i++) rows.Add(차입행(x[i, 0], "이자지급", "0", x[i, 1], org, "4.5"));
            rows.Add(차입행("2028-03-03", "상환", "0", "0", org, "4.5"));
            return rows;
        }

        static void 연장스케줄업로드(string group)
        {
            Console.WriteLine("\n[WEB-19c] 차입건 목록·연장스케줄 업로드·등록 전 금액 고치기 (ADR-0023)");
            Res r = Get("/api/loans");
            Check("차입건 목록 200", r.Status, 200);
            Has("차입처A 회차 4", r.Text, "\"group\":\"" + group + "\",\"org\":\"가짜PF\",\"name\":\"가짜 차입\",\"short\":\"가짜PF\",\"start\":\"2026-09-04\",\"face\":1500000000,\"rate\":4.2,\"maturity\":\"2027-09-03\",\"count\":4");
            Has("차입처B 도 있음", r.Text, "\"org\":\"가짜B차입\",\"name\":\"가짜B차입\",\"short\":\"\"");
            string y26 = Get("/api/year?y=2026").Text;
            Has("차입 회차 표시", y26, "\"loan\":true");
            Has("일반 항목은 차입 아님", y26, "\"loan\":false");

            byte[] ext = 엑셀(시트("연장", 연장A("차입처A")), 시트("다른 곳", 연장A("차입처Z")));
            Check("없는 차입건 404", 올리기("?mode=preview&extend=item-9999", ext, "연장.xlsx").Status, 404);
            r = 올리기("?mode=preview&extend=" + group, ext, "연장.xlsx");
            Check("연장 미리보기 200", r.Status, 200);
            Has("연장 대상", r.Text, "\"extend\":{\"group\":\"" + group + "\",\"org\":\"가짜PF\",\"maturity\":\"2027-09-03\",\"count\":4}");
            Has("회차 번호 이어짐 (5회차)", r.Text, "\"no\":5,");
            Has("6회차", r.Text, "\"no\":6,");
            Has("시작이 만기에서 이어짐", r.Text, "{\"label\":\"새 스케줄 시작\",\"value\":\"2027-09-03 (지금 만기에서 이어짐)\",\"level\":\"ok\"}");
            Has("이율 바뀜 경고", r.Text, "{\"label\":\"이율\",\"value\":\"4.2% → 4.5%\",\"level\":\"warn\"}");
            Has("다른 거래처 시트는 건너뜀", r.Text, "\"sheet\":\"다른 곳\",\"reason\":\"다른 거래처 (차입처Z)");
            Lacks("미리보기는 저장 안 함", Get("/api/items").Text, group + "-202712");

            r = 올리기("?mode=save&extend=" + group + "&ov=" + E("2027-12-03|16,875,001|연장") + "&ov=" + E("2028-03-03|16875000|연장"), ext, "연장.xlsx");
            Check("연장 저장 200", r.Status, 200);
            Has("2회차 붙음", r.Text, "\"added\":2");
            string items = Get("/api/items").Text;
            Has("연장 회차 이름도 번호 없이", items, "\"id\":\"" + group + "-202712\",\"org\":\"가짜PF\",\"name\":\"차입금 이자\"");
            Has("6회차 (2028)", items, "\"id\":\"" + group + "-202803\"");
            Has("고친 금액으로 등록 (2027 합계 = 2~4회차 + 16,875,001)", Get("/api/group?y=2027&group=" + group).Text, "\"total\":64125001");
            Has("고치지 않은 회차는 스케줄 합", Get("/api/group?y=2028&group=" + group).Text, "\"total\":16875000");
            using (Store db = Store.Open(DataPaths.Db(DataDir)))
            {
                Check("고친 금액 출처", db.LoadAmounts()["2027\t" + group + "-202712"].출처, "ERP 차입스케줄 (고침)");
                Check("그대로인 금액 출처", db.LoadAmounts()["2028\t" + group + "-202803"].출처, "ERP 차입스케줄");
                Check("기록 동작 '연장'", db.LoadEvents(2027, group + "-202712")[0].동작, "연장");
            }
            r = Get("/api/loans");
            Has("만기·이율 갱신, 차입일은 그대로", r.Text, "\"org\":\"가짜PF\",\"name\":\"가짜 차입\",\"short\":\"가짜PF\",\"start\":\"2026-09-04\",\"face\":1500000000,\"rate\":4.5,\"maturity\":\"2028-03-03\",\"count\":6");

            r = 올리기("?mode=save&extend=" + group, ext, "연장.xlsx");
            Has("같은 연장을 다시 올리면 새 회차 0", r.Text, "\"added\":0");
            Has("다시 올려도 번호 그대로 (5회차부터)", r.Text, "\"no\":5,");
            Has("차입건 회차 수 6", r.Text, "\"count\":6}");
            Check("잘못된 고친 금액 400", 올리기("?mode=save&extend=" + group + "&ov=" + E("12/3|1|연장"), ext, "연장.xlsx").Status, 400);
            Check("음수 고친 금액 400", 올리기("?mode=preview&ov=" + E("2027-12-03|-1|연장"), ext, "연장.xlsx").Status, 400);
        }

        static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }
}
