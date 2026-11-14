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
        Application.Run(new MainForm(new Workspace(AppPaths.Default), folder));
    }
}
