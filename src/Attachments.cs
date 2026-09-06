using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PaymentAlert
{
    /// <summary>
    /// 증빙 파일 보관소.
    /// 목록은 data/attachments.tsv 에, 실제 파일은 증빙/{연도}/{id}/ 아래에 둔다.
    /// </summary>
    public class AttachmentStore
    {
        readonly string indexPath;
        readonly string rootDir;

        /// <summary>(연도, id) -> 첨부 목록</summary>
        readonly Dictionary<string, List<Attachment>> byKey =
            new Dictionary<string, List<Attachment>>(StringComparer.Ordinal);

        public AttachmentStore(string indexPath, string rootDir)
        {
            this.indexPath = indexPath;
            this.rootDir = rootDir;
        }

        public string RootDir { get { return rootDir; } }

        public void Load()
        {
            byKey.Clear();
            foreach (var row in Tsv.Read(indexPath))
            {
                string id = Tsv.Get(row, "id");
                int year = Tsv.GetInt(row, "연도", 0);
                string saved = Tsv.Get(row, "저장파일");
                if (id.Length == 0 || year == 0 || saved.Length == 0) continue;

                var a = new Attachment();
                a.연도 = year;
                a.Id = id;
                a.단계 = Tsv.Get(row, "단계");
                a.저장파일 = saved;
                a.원본파일명 = Tsv.Get(row, "원본파일명");
                DateTime? d = Tsv.GetDate(row, "첨부일시");
                a.첨부일시 = d.HasValue ? d.Value : DateTime.MinValue;

                Add(a);
            }
        }

        void Add(Attachment a)
        {
            List<Attachment> list;
            if (!byKey.TryGetValue(a.Key, out list))
            {
                list = new List<Attachment>();
                byKey[a.Key] = list;
            }
            list.Add(a);
        }

        public List<Attachment> For(int year, string id)
        {
            List<Attachment> list;
            if (byKey.TryGetValue(year + "\t" + id, out list)) return list;
            return new List<Attachment>();
        }

        public int CountFor(int year, string id) { return For(year, id).Count; }

        /// <summary>해당 건의 증빙 폴더 경로. 파일이 없어도 경로는 돌려준다.</summary>
        public string FolderFor(int year, string id)
        {
            return Path.Combine(rootDir, year.ToString(CultureInfo.InvariantCulture), Safe(id));
        }

        /// <summary>
        /// 원본 파일을 증빙 폴더로 복사하고 목록에 추가한다.
        /// 원본이 나중에 옮겨지거나 지워져도 증빙은 남아야 하므로 링크가 아니라 복사다.
        /// </summary>
        public Attachment Attach(int year, string id, string stage, string sourcePath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("첨부할 파일을 찾을 수 없습니다.", sourcePath);

            string folder = FolderFor(year, id);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string originalName = Path.GetFileName(sourcePath);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string target = Path.Combine(folder,
                string.Format("{0}_{1}_{2}", Safe(stage), stamp, Safe(originalName)));

            // 같은 초에 두 번 붙이면 이름이 겹칠 수 있다.
            int n = 2;
            while (File.Exists(target))
            {
                target = Path.Combine(folder,
                    string.Format("{0}_{1}_{2}_{3}", Safe(stage), stamp, n, Safe(originalName)));
                n++;
            }

            File.Copy(sourcePath, target);

            var a = new Attachment();
            a.연도 = year;
            a.Id = id;
            a.단계 = stage;
            a.저장파일 = MakeRelative(target);
            a.원본파일명 = originalName;
            a.첨부일시 = DateTime.Now;
            Add(a);
            Save();
            return a;
        }

        /// <summary>목록에서 빼고 복사본도 지운다.</summary>
        public void Remove(Attachment a)
        {
            List<Attachment> list;
            if (byKey.TryGetValue(a.Key, out list)) list.Remove(a);

            try
            {
                string full = FullPath(a);
                if (File.Exists(full)) File.Delete(full);
            }
            catch { /* 파일을 못 지워도 목록에서는 빠져야 한다 */ }

            Save();
        }

        public string FullPath(Attachment a)
        {
            if (Path.IsPathRooted(a.저장파일)) return a.저장파일;
            return Path.Combine(rootDir, a.저장파일);
        }

        public void Save()
        {
            var all = new List<Attachment>();
            foreach (var list in byKey.Values) all.AddRange(list);

            all.Sort(delegate(Attachment x, Attachment y)
            {
                int c = x.연도.CompareTo(y.연도);
                if (c != 0) return c;
                c = string.Compare(x.Id, y.Id, StringComparison.Ordinal);
                if (c != 0) return c;
                return x.첨부일시.CompareTo(y.첨부일시);
            });

            var rows = new List<string[]>();
            foreach (Attachment a in all)
            {
                rows.Add(new string[] {
                    a.연도.ToString(CultureInfo.InvariantCulture),
                    a.Id,
                    a.단계 ?? "",
                    a.저장파일,
                    a.원본파일명 ?? "",
                    a.첨부일시 == DateTime.MinValue ? "" : a.첨부일시.ToString("yyyy-MM-dd HH:mm")
                });
            }

            Tsv.Write(indexPath,
                new string[] { "연도", "id", "단계", "저장파일", "원본파일명", "첨부일시" },
                rows,
                new string[] { "증빙 파일 목록입니다. 프로그램이 관리하므로 직접 편집하지 마세요." });
        }

        string MakeRelative(string full)
        {
            string root = rootDir.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length);
            return full;
        }

        /// <summary>파일 이름에 쓸 수 없는 문자를 걷어낸다.</summary>
        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char ch in s)
            {
                bool isBad = false;
                foreach (char b in bad) { if (ch == b) { isBad = true; break; } }
                sb.Append(isBad ? '_' : ch);
            }
            string r = sb.ToString().Trim();
            return r.Length == 0 ? "_" : r;
        }
    }
}
