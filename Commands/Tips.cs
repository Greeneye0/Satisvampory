using VampireCommandFramework;

namespace Satisvampory.Commands
{
    /// <summary>
    /// In-game misuse tips (1.0.114). Every usage error names the most likely intended command
    /// with a concrete example instead of a bare grammar line.
    /// </summary>
    internal static class Tips
    {
        const string W = "<color=white>";
        const string E = "</color>";

        public static string GroupForms(string name = null)
        {
            var n = string.IsNullOrWhiteSpace(name) ? "Tailor1" : name;
            return "Group commands:\n"
                + $"  create: {W}.s group create {n}{E}   (also {W}.s group add {n}{E})\n"
                + $"  add items: {W}.s group {n} add \"Coarse Thread\", Silk{E}   (quote names with spaces, commas or spaces between items)\n"
                + $"  remove items: {W}.s group {n} remove Silk{E}\n"
                + $"  show: {W}.s group {n}{E}   detail: {W}.s group {n} full{E}\n"
                + $"  delete / restore: {W}.s group delete {n}{E}   {W}.s group restore tailoring{E}";
        }

        /// <summary>".s group X add" with no items, or ".s group X something".</summary>
        public static string GroupTwoWords(string name, string option, bool groupExists)
        {
            var opt = (option ?? "").Trim().ToLowerInvariant();
            if (opt is "add" or "remove" or "rem" or "del")
                return $"You gave no items. Add or remove items like {W}.s group {name} {opt} \"Coarse Thread\", Silk{E}. Quote names with spaces.";
            if (!groupExists)
                return $"No group named {W}{name}{E}. Create it with {W}.s group create {name}{E}, then add items with {W}.s group {name} add \"Coarse Thread\"{E}.";
            return $"Did not understand {W}{option}{E}. " + GroupForms(name);
        }

        public static string GroupVerbs() => "Create, delete, or restore a group:\n" + GroupForms();

        public static string GroupModify(string name)
            => $"Add or remove items: {W}.s group {name} add \"Coarse Thread\", Silk{E}  or  {W}.s group {name} remove Silk{E}. Quote names with spaces; commas or spaces separate items.";

        public static string ItemsNotFound(string missing)
            => $"No items found matching: {missing}. Tips: quote names with spaces ({W}\"Iron Ore\"{E}), separate items with commas, use {W}.fi <part of name>{E} to search, or an alias ({W}.sg alias{E}).";

        public static string AliasForms()
            => $"Castle aliases (yours): {W}.s alias{E} lists, {W}.s alias add ci \"Copper Ingot\"{E} adds, {W}.s alias del ci{E} removes. Server-wide (admin): {W}.sg alias add / del{E}. Aliases work in .fi, .pull, chest plates, and --exclusions.";
    }
}
