using Microsoft.AspNetCore.Hosting;
using NativeSceneAutomation.Board;
using NativeSceneAutomation.Board.LedStrip;
using NativeSceneAutomation.Command;
using NativeSceneAutomation.Components;
using NativeSceneAutomation.Helpers;
using NativeSceneAutomation.Models;
using NativeSceneAutomation.Workflows;
using NativeSceneAutomation.Workflows.Steps;
using Serilog;
using System.Reflection;
using WorkflowCore.Interface;

namespace NativeSceneAutomation
{
    public class Program
    {
        public static void Main(string[] args)
        {
            PathHelper.SetExecutingAsCurrentDirectory();

            var logConfiguration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(PathHelper.ExecutingDirectory, Constants.ConfigLogger))
                .Build();

            Log.Logger = new LoggerConfiguration()
                            .ReadFrom.Configuration(logConfiguration)
                            .CreateBootstrapLogger();

            string version = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(version) && version.Length > 2)
                version = version.Substring(2);

            Log.Information("Starting up {name} - {version}", Assembly.GetExecutingAssembly().GetName().Name, version);

            var builder = WebApplication.CreateBuilder(args);

            builder.Host.UseSystemd()
                        .UseWindowsService()
                        .UseSerilog((context, _, config) =>
                        {
                            config.ReadFrom.Configuration(logConfiguration)
                                .Enrich.FromLogContext();
                        });

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                            .AddInteractiveServerComponents();

            builder.Services.AddSingleton<IHWService, HWService>()
                            .AddHostedService<Worker>()
                            .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Worker>());

            builder.Services.AddWorkflow();
            builder.Services.AddSingleton<WorkflowsExecutionState>();
            builder.Services.AddTransient<SunriseStep>();
            builder.Services.AddTransient<SunsetStep>();
            builder.Services.AddTransient<WaitStep>();
            builder.Services.AddTransient<TreeRotationStep>();

            var app = builder.Build();

            // register workflow
            var workflowHost = app.Services.GetRequiredService<IWorkflowHost>();
            workflowHost.RegisterWorkflow<NativeSceneWorkflow, NativeSceneWorkflowInputModel>();
            workflowHost.RegisterWorkflow<AnimationWorkflow, bool>();
            workflowHost.OnStepError += (workflow, step, exception) =>
            {
                Log.Error($"Error in workflow {workflow} step {step}", workflow.Id, step.Id);
            };

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
               .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
