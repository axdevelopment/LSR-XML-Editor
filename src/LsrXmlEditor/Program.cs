using System.Globalization;
using LsrXmlEditor.Core.Storage;
using LsrXmlEditor.Forms;
using LsrXmlEditor.Infrastructure;
using LsrXmlEditor.Services;

namespace LsrXmlEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var culture = Format.Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Dialogs.Error(null, e.Exception.Message);

        var folder = args.FirstOrDefault(Directory.Exists);
        var workspace = new Workspace(AppPaths.Default);
        UiTheme.Apply(workspace.Settings, startup: true);
        var context = new ApplicationContext();

        MainForm Create(MainFormState? state)
        {
            var form = new MainForm(workspace, folder, state);
            form.AppearanceChanged += (_, _) =>
            {
                var next = Create(form.CaptureState());
                context.MainForm = next;
                next.Show();
                form.CloseForRebuild();
            };
            return form;
        }

        context.MainForm = Create(null);
        Application.Run(context);
    }
}
