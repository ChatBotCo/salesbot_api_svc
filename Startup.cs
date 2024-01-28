﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SalesBotApi.Models;
using Microsoft.Azure.Storage;
using Microsoft.AspNetCore.Diagnostics;
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IO;

namespace SalesBotApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();
            services.AddControllers();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SalesChat.bot API", Version = "v1" });
            });
            services.AddSingleton<CosmosDbService>();
            services.AddSingleton<EmailService>();
            services.AddSingleton<QueueService>();
            services.AddSingleton<SharedQueriesService>();
            services.AddSingleton<JwtService>();
            services.AddSingleton<SemanticKernelService>();
            services.AddSingleton<MemoryStoreService>();
            services.AddSingleton<AzureOpenAIEmbeddings>();
            services.AddSingleton<WebpageProcessor>();
            services.AddHostedService<QueueBackgroundService>();
            services.AddApplicationInsightsTelemetry();


            services.AddCors(options =>
                {
                    options.AddPolicy("AllowSpecificOrigin",
                        builder => builder.WithOrigins("*")
                                          .AllowAnyHeader()
                                          .AllowAnyMethod());
                });
            services.AddHttpClient();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SalesChat.bot API V1");
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection();

            app.UseDefaultFiles();

            app.UseStaticFiles();

            app.UseCors("AllowSpecificOrigin");

            app.UseRouting();

            app.UseAuthorization();

            app.UseExceptionHandler(appBuilder =>
            {
                appBuilder.Run(async context =>
                {
                    var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
                    if (exceptionHandlerPathFeature?.Error is Exception ex)
                    {
                        // Log the exception
                        logger.LogError(ex, "Unhandled exception occurred.");
                        
                        // Respond with a list of files in the current directory
                        var currentDirectory = Directory.GetCurrentDirectory();
                        var files = Directory.GetFileSystemEntries(currentDirectory);
                        var filesList = string.Join("\n, ", files);

                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync("An unexpected fault happened. Try again later.\n");
                        await context.Response.WriteAsync($"Current Directory: {currentDirectory}\nFiles: {filesList}");
                        
                        // Respond with a list of files in the current directory
                        var ContentRootPath = env.ContentRootPath;
                        var filesContentRootPath = Directory.GetFileSystemEntries(ContentRootPath);
                        var filesListContentRootPath = string.Join("\n, ", filesContentRootPath);
                        await context.Response.WriteAsync($"\n ContentRootPath: {ContentRootPath}\nFiles: {filesListContentRootPath}");

                        var userQuestionPluginDirectoryPath = Path.Combine(env.ContentRootPath, "Plugins", "SalesBot");
                        var filesuserQuestionPluginDirectoryPath = Directory.GetFileSystemEntries(userQuestionPluginDirectoryPath);
                        var filesListuserQuestionPluginDirectoryPath = string.Join("\n, ", filesuserQuestionPluginDirectoryPath);
                        await context.Response.WriteAsync($"\n userQuestionPluginDirectoryPath: {userQuestionPluginDirectoryPath}\nFiles: {filesListuserQuestionPluginDirectoryPath}");
                    }
                });
            });


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
