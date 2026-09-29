namespace ExtraDim
{
    // English by default; Chinese on request.
    static class Lang
    {
        public static bool Zh;

        public static string T(string en, string zh) { return Zh ? zh : en; }
    }
}
