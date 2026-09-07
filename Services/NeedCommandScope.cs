using System.Reflection;
using VampireCommandFramework;

namespace Satisvampory.Services
{
    // A bare number is valid only for the next executed command after a need list.
    internal sealed class NeedCommandScope : CommandMiddleware
    {
        internal static readonly NeedCommandScope Instance = new();
        public override void BeforeExecute(ICommandContext context, CommandAttribute attribute, MethodInfo method)
        {
            if (context is ChatCommandContext ctx && method.Name != "SPick")
                NeedReport.ClearNumber(ctx.Event.User.PlatformId);
        }
    }
}
