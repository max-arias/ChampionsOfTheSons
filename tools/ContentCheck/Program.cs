using ChampionsOfTheSons.Core.Content;

var folder = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "content");
var db = ContentDb.LoadFolder(Path.GetFullPath(folder));
Console.WriteLine(db.Summary());
foreach (var e in db.Errors) Console.WriteLine("  " + e);
Console.WriteLine($"manual: {db.ItemStats.Values.Count(s => s.Manual)} item stats, {db.Items.Values.Count(i => i.Manual)} items, {db.Perks.Values.Count(p => p.Manual)} perks");
return db.Errors.Count == 0 ? 0 : 1;
