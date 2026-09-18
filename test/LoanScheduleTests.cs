using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using PaymentAlert;

namespace PaymentAlert.Tests
{
    /// <summary>
    /// 차입 스케줄 읽기·분기 묶기 시험 (ADR-0023). 값은 모두 가짜다 — 실제 차입 조건을 넣지 않는다.
    /// 모양은 '차입관리스케줄 양식.xlsx' 와 같다: 월말 경과이자 행 + 분기 달의 지급일 조각 행 + 상환 행 + 합계행.
    /// </summary>
    static partial class TestRunner
    {
        static readonly string[] 머리 = {
            "No.", "기준일자", "순번", "전표일자", "자금요청일", "현금흐름구분", "액면금액", "상환금액",
            "액면이자금액", "액면이자율", "전표번호", "잔액관리번호", "지급전표번호", "전표적요", "거래처명",
            "거래처 지급계좌번호", "구ERP여부", "마감여부" };

        static string[] 행(string date, string kind, string face, string interest, string org)
        {
            return 행(date, kind, face, interest, org, "4.2000000000000002");
        }

        static string[] 행(string date, string kind, string face, string interest, string org, string rate)
        {
            return new[] { "1", date, "1", date, date, kind, face, "0", interest, rate, "", "", "", "", org, "", "", "" };
        }

        /// <summary>액면 15억·4.2%·차입일 2026-09-04. 분기 이자는 15,750,000 으로 떨어진다.</summary>
        static Sheet 가짜스케줄()
        {
            var s = new Sheet();
            s.이름 = "차입처A";
            s.행.Add(머리);
            s.행.Add(행("2026-09-04", "신규", "1500000000", "0", "차입처A"));
            string[][] 이자 = {
                new[] { "2026-09-30", "4660274" }, new[] { "2026-10-31", "5350685" }, new[] { "2026-11-30", "5178082" }, new[] { "2026-12-04", "560959" },
                new[] { "2026-12-31", "4832877" }, new[] { "2027-01-31", "5350685" }, new[] { "2027-02-28", "4832877" }, new[] { "2027-03-04", "733561" },
                new[] { "2027-03-31", "4832877" }, new[] { "2027-04-30", "5178082" }, new[] { "2027-05-31", "5350685" }, new[] { "2027-06-04", "388356" },
                new[] { "2027-06-30", "4660274" }, new[] { "2027-07-31", "5350685" }, new[] { "2027-08-31", "5350685" }, new[] { "2027-09-03", "388356" } };
            foreach (string[] x in 이자) s.행.Add(행(x[0], "이자지급", "0", x[1], "차입처A"));
            s.행.Add(행("2027-09-03", "상환", "0", "0", "차입처A"));
            s.행.Add(new[] { "Σ", "", "", "", "", "", "1500000000", "1500000000", "63000000", "", "", "", "", "", "", "", "", "" });
            s.행.Add(new[] { "" });
            return s;
        }

        static string 지급일들(LoanPlan p)
        {
            var list = new List<string>();
            foreach (LoanPayment x in p.지급) list.Add(x.지급일.ToString("yyyy-MM-dd"));
            return string.Join(",", list.ToArray());
        }

        static string 형식오류문구(Sheet s)
        {
            try { LoanSchedule.Parse(s); return null; }
            catch (LoanSchedule.형식오류 ex) { return ex.Message; }
        }

        static void 차입스케줄시험()
        {
            Console.WriteLine("\n[13] 차입 스케줄 → 분기 지급 (ADR-0023)");
            Sheet s = 가짜스케줄();
            CheckTrue("양식 알아봄", LoanSchedule.양식인가(s));
            LoanPlan p = LoanSchedule.Parse(s);
            Check("거래처", p.거래처, "차입처A");
            Check("차입일 = '신규' 행", p.차입일.ToString("yyyy-MM-dd"), "2026-09-04");
            Check("액면", p.액면, 1500000000m);
            Check("이율 (긴 소수 정리)", p.이율, 4.2m);
            Check("만기 = '상환' 행 날짜 그대로", p.만기.Value.ToString("yyyy-MM-dd"), "2027-09-03");
            Check("분기 4회", p.지급.Count, 4);
            Check("지급일 = 조각 행 날짜 (9/4 토 → ERP 9/3 그대로)", 지급일들(p), "2026-12-04,2027-03-04,2027-06-04,2027-09-03");
            bool 모두같음 = true;
            foreach (LoanPayment x in p.지급) if (x.금액 != 15750000m) 모두같음 = false;
            CheckTrue("분기 지급액 = 15,750,000 (액면×이율÷4)", 모두같음);
            Check("1회차는 행 4개 (9/30·10/31·11/30·12/4)", p.지급[0].행.Count, 4);
            Check("1회차 기간 시작 = 차입일", p.지급[0].기간시작.ToString("yyyy-MM-dd"), "2026-09-04");
            Check("2회차 첫 행 = 12/31 (조각 다음부터)", p.지급[1].행[0].날짜.ToString("yyyy-MM-dd"), "2026-12-31");
            Check("이자 합계 = 합계행", p.이자합계, 63000000m);
            Check("경고 없음", p.경고.Count, 0);
            Check("분기 예상", p.분기예상, 15750000m);

            Console.WriteLine("\n[13b] 지급일 고르기");
            // 조각 행이 빠지면 월말 행(11/30)이 지급일로 잡히고, 금액이 예상과 달라 경고한다 — 조용히 틀리지 않는다.
            Sheet noStub = 가짜스케줄();
            noStub.행.RemoveAt(5);   // 2026-12-04 조각 (0 머리, 1 신규, 2 9/30, 3 10/31, 4 11/30, 5 12/4)
            LoanPlan p2 = LoanSchedule.Parse(noStub);
            Check("조각 없으면 11/30 이 1회차", p2.지급[0].지급일.ToString("yyyy-MM-dd"), "2026-11-30");
            CheckTrue("1회차 예상과 다름 표시", p2.지급[0].예상과다름);
            CheckTrue("경고 문구", p2.경고.Count > 0 && p2.경고[0].Contains("1회차"));

            // ERP 가 휴일이라 뒤로 민 지급일(1/16 토 → 1/18)도 ±7일 안이면 찾는다.
            var b = new Sheet();
            b.이름 = "차입처B";
            b.행.Add(머리);
            b.행.Add(행("2026-10-16", "신규", "500000000", "0", "차입처B"));
            b.행.Add(행("2026-10-31", "이자지급", "0", "100", "차입처B"));
            b.행.Add(행("2026-11-30", "이자지급", "0", "100", "차입처B"));
            b.행.Add(행("2026-12-31", "이자지급", "0", "100", "차입처B"));
            b.행.Add(행("2027-01-18", "이자지급", "0", "50", "차입처B"));
            LoanPlan pb = LoanSchedule.Parse(b);
            Check("휴일로 민 지급일", 지급일들(pb), "2027-01-18");
            Check("합 350", pb.지급[0].금액, 350m);
            Check("만기 행 없으면 null", pb.만기.HasValue, false);

            // 차입일이 월말이면 조각이 없고 월말 행이 곧 지급일. 1/31 + 3개월 = 4/30.
            var e = new Sheet();
            e.행.Add(머리);
            e.행.Add(행("20260131", "신규", "1,000,000", "0", ""));
            foreach (string d in new[] { "20260228", "20260331", "20260430", "20260531", "20260630", "20260731" })
                e.행.Add(행(d, "이자지급", "0", "1,000", ""));
            e.이름 = "시트이름";
            LoanPlan pe = LoanSchedule.Parse(e);
            Check("월말 차입: 지급일 4/30·7/31", 지급일들(pe), "2026-04-30,2026-07-31");
            Check("월말 차입: 분기 합 3,000", pe.지급[1].금액, 3000m);
            Check("거래처가 비면 시트 이름", pe.거래처, "시트이름");

            // 3/31 월말 차입: 차입일 당일 하루치 행이 있고, 조각 행 없이 분기 달(6월·9월) 월말 행이 그 달 전체 경과이자이자 지급일.
            var me = new Sheet();
            me.이름 = "차입처C";
            me.행.Add(머리);
            me.행.Add(행("2026-03-31", "신규", "1000000000", "0", "차입처C", "4.8"));
            string[,] meRows = {
                { "2026-03-31", "131507" }, { "2026-04-30", "3945205" }, { "2026-05-31", "4076712" }, { "2026-06-30", "3945205" },
                { "2026-07-31", "4076712" }, { "2026-08-31", "4076712" }, { "2026-09-30", "3945205" } };
            for (int i = 0; i < meRows.GetLength(0); i++) me.행.Add(행(meRows[i, 0], "이자지급", "0", meRows[i, 1], "차입처C", "4.8"));
            LoanPlan pm = LoanSchedule.Parse(me);
            Check("월말 차입: 지급일 6/30·9/30", 지급일들(pm), "2026-06-30,2026-09-30");
            Check("월말 차입: 1회차에 차입일 당일 행 포함 (4행)", pm.지급[0].행.Count, 4);
            Check("월말 차입: 1회차 = 3/31 하루 + 4·5월 + 6월 전체", pm.지급[0].금액, 12098629m);
            Check("월말 차입: 2회차 = 7·8월 + 9월 전체", pm.지급[1].금액, 12098629m);
            Check("월말 차입: 1회차 기간 시작 = 차입일", pm.지급[0].기간시작.ToString("yyyy-MM-dd"), "2026-03-31");
            Check("월말 차입: 1% 안쪽 차이는 경고 없음", pm.경고.Count, 0);

            Sheet early = 가짜스케줄();
            early.행.Insert(2, 행("2026-09-03", "이자지급", "0", "1", "차입처A", "4.2"));
            CheckTrue("차입일보다 이른 이자 행은 거절", (형식오류문구(early) ?? "").Contains("차입일(2026-09-04) 이전"));
            Check("이율 없으면 예상 없음 (4.2 있음)", pe.분기예상.HasValue, true);

            Console.WriteLine("\n[13c] 쓸 수 없는 스케줄은 이유와 함께 거절");
            var nohead = new Sheet();
            nohead.행.Add(new[] { "일자", "금액" });
            CheckTrue("양식 아님", !LoanSchedule.양식인가(nohead));
            CheckTrue("양식 아님 오류", (형식오류문구(nohead) ?? "").Contains("양식이 아닙니다"));

            Sheet noNew = 가짜스케줄();
            noNew.행.RemoveAt(1);
            CheckTrue("신규 행 없음", (형식오류문구(noNew) ?? "").Contains("'신규'"));

            Sheet twoNew = 가짜스케줄();
            twoNew.행.Insert(2, 행("2026-09-05", "신규", "1", "0", "차입처A"));
            CheckTrue("신규 두 번 → 시트 나누라고", (형식오류문구(twoNew) ?? "").Contains("시트를 나눠"));

            Sheet badDate = 가짜스케줄();
            badDate.행[3][1] = "9월 말";
            CheckTrue("날짜 못 읽으면 행 번호", (형식오류문구(badDate) ?? "").StartsWith("4행"));

            Sheet leftover = 가짜스케줄();
            leftover.행.Insert(18, 행("2027-10-20", "이자지급", "0", "10", "차입처A"));
            CheckTrue("마지막 지급일 뒤 남는 행", (형식오류문구(leftover) ?? "").Contains("남습니다"));

            Sheet gap = 가짜스케줄();
            gap.행.RemoveAt(4); gap.행.RemoveAt(4);   // 11/30·12/4 — 12/4 앞뒤 7일 안에 이자 행이 없다
            CheckTrue("회차 지급일 못 찾음", (형식오류문구(gap) ?? "").Contains("회차 지급일"));

            Sheet total = 가짜스케줄();
            total.행[total.행.Count - 2][8] = "63000001";
            CheckTrue("합계행 불일치는 경고", LoanSchedule.Parse(total).경고.Count == 1);

            Console.WriteLine("\n[13d] 값 읽기");
            DateTime d1;
            CheckTrue("yyyyMMdd", LoanSchedule.날짜읽기("20260904", out d1) && d1 == new DateTime(2026, 9, 4));
            CheckTrue("yyyy.MM.dd", LoanSchedule.날짜읽기("2026.09.04", out d1) && d1 == new DateTime(2026, 9, 4));
            CheckTrue("엑셀 일련번호 46269", LoanSchedule.날짜읽기("46269", out d1) && d1 == new DateTime(2026, 9, 4));
            CheckTrue("시각 붙은 날짜", LoanSchedule.날짜읽기("2027-01-18 00:00:00", out d1) && d1 == new DateTime(2027, 1, 18));
            CheckTrue("작은 숫자는 날짜 아님", !LoanSchedule.날짜읽기("12", out d1));
            decimal m;
            CheckTrue("쉼표 금액", LoanSchedule.금액읽기("12,500,000", out m) && m == 12500000m);
            CheckTrue("원 붙은 금액", LoanSchedule.금액읽기("1,000원", out m) && m == 1000m);
            CheckTrue("엑셀 실수 표기", LoanSchedule.금액읽기("12500000.0", out m) && m == 12500000m);
            CheckTrue("원 미만은 거절", !LoanSchedule.금액읽기("12.5", out m));
            CheckTrue("빈 칸은 거절", !LoanSchedule.금액읽기("", out m));

            파일읽기시험();
        }

        // ── 파일 읽기 ──────────────────────────────────────────

        static void Zip넣기(ZipArchive z, string name, string xml)
        {
            ZipArchiveEntry e = z.CreateEntry(name);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(xml);
        }

        /// <summary>엑셀이 만드는 최소 구성의 xlsx. 시트 둘: 공유 글자·인라인 글자·숫자·빈 칸 건너뛰기.</summary>
        public static byte[] 가짜엑셀()
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    const string M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                    Zip넣기(z, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
                    Zip넣기(z, "xl/workbook.xml",
                        "<?xml version=\"1.0\"?><workbook xmlns=\"" + M + "\" xmlns:r=\"" + R + "\"><sheets>" +
                        "<sheet name=\"첫 시트\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"둘째\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                    Zip넣기(z, "xl/_rels/workbook.xml.rels",
                        "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"x\" Target=\"worksheets/sheet1.xml\"/>" +
                        "<Relationship Id=\"rId2\" Type=\"x\" Target=\"/xl/worksheets/sheet2.xml\"/></Relationships>");
                    Zip넣기(z, "xl/sharedStrings.xml",
                        "<?xml version=\"1.0\"?><sst xmlns=\"" + M + "\"><si><t>기준일자</t></si>" +
                        "<si><r><t>현금</t></r><r><t>흐름구분</t></r><rPh><t>ヨミ</t></rPh></si><si><t xml:space=\"preserve\"> 신규 </t></si></sst>");
                    Zip넣기(z, "xl/worksheets/sheet1.xml",
                        "<?xml version=\"1.0\"?><worksheet xmlns=\"" + M + "\"><sheetData>" +
                        "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"D1\" t=\"inlineStr\"><is><t>액면이자금액</t></is></c></row>" +
                        "<row r=\"3\"><c r=\"A3\"><v>46269</v></c><c r=\"B3\" t=\"s\"><v>2</v></c><c r=\"D3\"><v>1500000000</v></c><c r=\"E3\" t=\"b\"><v>1</v></c></row>" +
                        "</sheetData></worksheet>");
                    Zip넣기(z, "xl/worksheets/sheet2.xml",
                        "<?xml version=\"1.0\"?><worksheet xmlns=\"" + M + "\"><sheetData><row r=\"1\"><c r=\"B1\" t=\"str\"><v>수식 결과</v></c></row></sheetData></worksheet>");
                }
                return ms.ToArray();
            }
        }

        static string 읽기오류문구(byte[] data, string name)
        {
            try { SheetReader.Read(data, name); return null; }
            catch (SheetReader.읽기오류 ex) { return ex.Message; }
        }

        static void 파일읽기시험()
        {
            Console.WriteLine("\n[14] ERP 파일 읽기 — xlsx·csv (ADR-0023)");
            List<Sheet> sheets = SheetReader.Read(가짜엑셀(), "스케줄.xlsx");
            Check("시트 2개", sheets.Count, 2);
            Check("시트 이름", sheets[0].이름 + "|" + sheets[1].이름, "첫 시트|둘째");
            Check("공유 글자", sheets[0].행[0][0], "기준일자");
            Check("서식 조각 이어 붙이기 (읽는 법 표시 제외)", sheets[0].행[0][1], "현금흐름구분");
            Check("빈 열 자리 채움 (C1)", sheets[0].행[0][2], "");
            Check("인라인 글자", sheets[0].행[0][3], "액면이자금액");
            Check("빈 행 자리 (2행)", sheets[0].행[1].Length, 0);
            Check("숫자는 그대로 (날짜 일련번호)", sheets[0].행[2][0], "46269");
            Check("공유 글자 공백 유지 (정리는 읽는 쪽)", sheets[0].행[2][1], " 신규 ");
            Check("큰 숫자", sheets[0].행[2][3], "1500000000");
            Check("참/거짓", sheets[0].행[2][4], "TRUE");
            Check("수식 결과 글자 · 절대 경로 대상", sheets[1].행[0][1], "수식 결과");
            Check("열 번호 AB12", SheetReader.열번호("AB12"), 27);
            Check("열 번호 A1", SheetReader.열번호("A1"), 0);
            Check("열 번호 틀림", SheetReader.열번호("12"), -1);

            string csv = "기준일자,현금흐름구분,액면이자금액,전표적요\r\n2026-09-04,신규,0,\"차입, \"\"A\"\"\"\r\n2026-09-30,이자지급,\"4,660,274\",\"여러\n줄\"";
            Sheet c = SheetReader.Read(Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes(csv)), "차입처C.csv")[0];
            Check("CSV 시트 이름 = 파일 이름", c.이름, "차입처C");
            Check("CSV BOM 제거", c.행[0][0], "기준일자");
            Check("CSV 따옴표 안 쉼표·따옴표", c.행[1][3], "차입, \"A\"");
            Check("CSV 따옴표 안 금액 쉼표", c.행[2][2], "4,660,274");
            Check("CSV 따옴표 안 줄바꿈", c.행[2][3], "여러\n줄");
            Check("CSV 행 수", c.행.Count, 3);

            Sheet c949 = SheetReader.Read(Encoding.GetEncoding(949).GetBytes("기준일자,현금흐름구분\n2026-09-04,신규\n"), "a.csv")[0];
            Check("CP949 CSV", c949.행[1][1], "신규");
            Check("끝 줄바꿈 뒤 빈 행 없음", c949.행.Count, 2);

            Sheet tab = SheetReader.Read(Encoding.UTF8.GetBytes("기준일자\t현금흐름구분\n2026-09-04\t신규"), "a.txt")[0];
            Check("탭 구분", tab.행[1][1], "신규");

            byte[] ole = new byte[512];
            ole[0] = 0xD0; ole[1] = 0xCF; ole[2] = 0x11; ole[3] = 0xE0;
            CheckTrue("암호·문서보안 파일 안내", (읽기오류문구(ole, "보안문서.xlsx") ?? "").Contains("문서보안"));
            CheckTrue("확장자만 xlsx", (읽기오류문구(Encoding.UTF8.GetBytes("a,b"), "x.xlsx") ?? "").Contains("엑셀 파일 형식이 아닙니다"));
            CheckTrue("빈 파일", (읽기오류문구(new byte[0], "a.csv") ?? "").Contains("빈 파일"));
            byte[] broken = 가짜엑셀();
            Array.Resize(ref broken, 60);
            CheckTrue("깨진 xlsx", 읽기오류문구(broken, "a.xlsx") != null);
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
