using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace PaymentAlert
{
    /// <summary>표 한 장. 엑셀은 시트 하나, CSV 는 파일 하나.</summary>
    public sealed class Sheet
    {
        public string 이름 = "";
        public readonly List<string[]> 행 = new List<string[]>();
    }

    /// <summary>
    /// ERP 에서 받은 엑셀(.xlsx)·CSV 를 글자 표로 읽는다 (ADR-0023).
    /// .NET Framework 에 들어 있는 압축·XML 기능만 쓴다 — 추가 DLL 없음.
    /// 숫자는 엑셀에 저장된 그대로의 글자(예: 날짜 일련번호 46269)로 돌려주고, 뜻은 읽는 쪽이 정한다.
    /// </summary>
    public static class SheetReader
    {
        public const long 최대크기 = 20L * 1024 * 1024;
        const long 풀린최대 = 64L * 1024 * 1024;   // 압축 폭탄 막기: 한 부분이 풀렸을 때의 한도
        const int 최대행 = 20000;
        const int 최대열 = 200;

        public sealed class 읽기오류 : Exception
        {
            public 읽기오류(string message) : base(message) { }
        }

        public static List<Sheet> Read(byte[] data, string fileName)
        {
            if (data == null || data.Length == 0) throw new 읽기오류("빈 파일입니다.");
            if (data.Length > 최대크기) throw new 읽기오류("20MB 보다 큰 파일은 읽지 않습니다.");

            if (data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
                return Xlsx(data);

            // D0 CF 11 E0: 옛 엑셀(.xls) 또는 암호·문서보안이 걸린 엑셀. 둘 다 내용을 읽을 수 없다.
            if (data.Length >= 4 && data[0] == 0xD0 && data[1] == 0xCF && data[2] == 0x11 && data[3] == 0xE0)
                throw new 읽기오류("암호가 걸렸거나 문서보안(DRM)이 적용된 파일, 또는 옛 엑셀(.xls) 파일이라 읽을 수 없습니다. " +
                                  "ERP 에서 CSV 로 받거나, 엑셀에서 암호·보안을 푼 뒤 .xlsx 로 저장해 올려 주세요.");

            string ext = (Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant();
            if (ext == ".xlsx" || ext == ".xlsm")
                throw new 읽기오류("엑셀 파일 형식이 아닙니다. 파일이 손상됐거나 확장자만 바뀐 파일입니다.");

            var sheet = Csv(Text(data));
            sheet.이름 = Path.GetFileNameWithoutExtension(fileName ?? "") ?? "";
            return new List<Sheet> { sheet };
        }

        // ── CSV ─────────────────────────────────────────────────

        /// <summary>UTF-8(BOM 유무) → 안 되면 한국어 윈도 코드(CP949). ERP 의 CSV 는 대개 CP949 다.</summary>
        static string Text(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return new UTF8Encoding(false).GetString(data, 3, data.Length - 3);
            try
            {
                return new UTF8Encoding(false, true).GetString(data);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(949).GetString(data);
            }
        }

        public static Sheet Csv(string text)
        {
            var sheet = new Sheet();
            if (string.IsNullOrEmpty(text)) return sheet;

            // 첫 줄에 쉼표가 없고 탭이 있으면 탭 구분으로 본다 (엑셀 '텍스트(탭 구분)' 저장).
            int eol = text.IndexOfAny(new[] { '\r', '\n' });
            string first = eol < 0 ? text : text.Substring(0, eol);
            char sep = first.IndexOf(',') < 0 && first.IndexOf('\t') >= 0 ? '\t' : ',';

            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                    continue;
                }
                if (ch == '"' && cell.Length == 0) { quoted = true; continue; }
                if (ch == sep) { row.Add(cell.ToString()); cell.Length = 0; continue; }
                if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Length = 0;
                    행추가(sheet, row);
                    row = new List<string>();
                    continue;
                }
                cell.Append(ch);
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                행추가(sheet, row);
            }
            return sheet;
        }

        static void 행추가(Sheet sheet, List<string> row)
        {
            if (sheet.행.Count >= 최대행) throw new 읽기오류("행이 너무 많습니다 (" + 최대행 + "행까지).");
            if (row.Count > 최대열) row.RemoveRange(최대열, row.Count - 최대열);
            sheet.행.Add(row.ToArray());
        }

        // ── XLSX ────────────────────────────────────────────────

        const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        static List<Sheet> Xlsx(byte[] data)
        {
            try
            {
                using (var ms = new MemoryStream(data, false))
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    XmlDocument wb = Part(zip, "xl/workbook.xml");
                    if (wb == null) throw new 읽기오류("엑셀 통합 문서 정보(workbook.xml)가 없습니다.");

                    var targets = new Dictionary<string, string>(StringComparer.Ordinal);
                    XmlDocument rels = Part(zip, "xl/_rels/workbook.xml.rels");
                    if (rels != null)
                    {
                        foreach (XmlElement r in rels.GetElementsByTagName("Relationship", NsPkgRel))
                        {
                            string t = r.GetAttribute("Target");
                            if (t.StartsWith("/")) t = t.TrimStart('/');
                            else t = "xl/" + t;
                            targets[r.GetAttribute("Id")] = t;
                        }
                    }

                    List<string> shared = SharedStrings(zip);
                    var result = new List<Sheet>();
                    foreach (XmlElement s in wb.GetElementsByTagName("sheet", NsMain))
                    {
                        string rid = s.GetAttribute("id", NsRel);
                        string path;
                        if (!targets.TryGetValue(rid, out path)) continue;
                        XmlDocument doc = Part(zip, path);
                        if (doc == null) continue;
                        Sheet sheet = SheetOf(doc, shared);
                        sheet.이름 = s.GetAttribute("name");
                        result.Add(sheet);
                    }
                    if (result.Count == 0) throw new 읽기오류("읽을 수 있는 시트가 없습니다.");
                    return result;
                }
            }
            catch (InvalidDataException)
            {
                throw new 읽기오류("엑셀 파일이 손상돼 열 수 없습니다.");
            }
            catch (XmlException)
            {
                throw new 읽기오류("엑셀 파일 안의 내용이 올바르지 않습니다.");
            }
        }

        static XmlDocument Part(ZipArchive zip, string path)
        {
            ZipArchiveEntry e = zip.GetEntry(path);
            if (e == null)
            {
                // 대소문자만 다른 이름으로 저장하는 프로그램이 있다.
                foreach (ZipArchiveEntry x in zip.Entries)
                    if (string.Equals(x.FullName, path, StringComparison.OrdinalIgnoreCase)) { e = x; break; }
            }
            if (e == null) return null;
            if (e.Length > 풀린최대) throw new 읽기오류("엑셀 파일 안의 내용이 너무 큽니다.");

            var doc = new XmlDocument();
            doc.XmlResolver = null;
            var settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;
            settings.MaxCharactersInDocument = 풀린최대;
            using (Stream s = e.Open())
            using (XmlReader r = XmlReader.Create(s, settings))
                doc.Load(r);
            return doc;
        }

        static List<string> SharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            XmlDocument doc = Part(zip, "xl/sharedStrings.xml");
            if (doc == null) return list;
            foreach (XmlElement si in doc.GetElementsByTagName("si", NsMain))
                list.Add(Runs(si));
            return list;
        }

        /// <summary>글자 조각(서식이 섞인 글자)을 이어 붙인다. 읽는 법 표시(rPh)는 뺀다.</summary>
        static string Runs(XmlElement parent)
        {
            var sb = new StringBuilder();
            foreach (XmlElement t in parent.GetElementsByTagName("t", NsMain))
            {
                if (t.ParentNode != null && t.ParentNode.LocalName == "rPh") continue;
                sb.Append(t.InnerText);
            }
            return sb.ToString();
        }

        static Sheet SheetOf(XmlDocument doc, List<string> shared)
        {
            var sheet = new Sheet();
            foreach (XmlElement row in doc.GetElementsByTagName("row", NsMain))
            {
                var cells = new List<string>();
                int next = 0;
                foreach (XmlNode n in row.ChildNodes)
                {
                    var c = n as XmlElement;
                    if (c == null || c.LocalName != "c") continue;
                    int col = 열번호(c.GetAttribute("r"));
                    if (col < 0) col = next;
                    next = col + 1;
                    if (col >= 최대열) continue;
                    while (cells.Count < col) cells.Add("");
                    cells.Add(값(c, shared));
                }

                // 빈 행도 자리를 지킨다 — 행 번호가 사용자 안내에 쓰인다.
                int rowNo;
                if (int.TryParse(row.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out rowNo))
                    while (sheet.행.Count < rowNo - 1) 행추가(sheet, new List<string>());
                행추가(sheet, cells);
            }
            return sheet;
        }

        static string 값(XmlElement c, List<string> shared)
        {
            string t = c.GetAttribute("t");
            XmlElement v = null;
            foreach (XmlNode n in c.ChildNodes)
                if (n.LocalName == "v") { v = (XmlElement)n; break; }

            if (t == "inlineStr")
            {
                foreach (XmlNode n in c.ChildNodes)
                    if (n.LocalName == "is") return Runs((XmlElement)n);
                return "";
            }
            if (v == null) return "";
            string raw = v.InnerText;
            if (t == "s")
            {
                int i;
                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) && i >= 0 && i < shared.Count
                    ? shared[i] : "";
            }
            if (t == "b") return raw == "1" ? "TRUE" : "FALSE";
            return raw;
        }

        /// <summary>"AB12" → 27 (0부터). 형식이 틀리면 -1.</summary>
        public static int 열번호(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return -1;
            int n = 0, i = 0;
            for (; i < cellRef.Length; i++)
            {
                char ch = char.ToUpperInvariant(cellRef[i]);
                if (ch < 'A' || ch > 'Z') break;
                n = n * 26 + (ch - 'A' + 1);
                if (n > 16384) return -1;
            }
            return i == 0 ? -1 : n - 1;
        }
    }
}
