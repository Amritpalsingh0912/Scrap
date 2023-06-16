using Azure.Messaging.ServiceBus;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using ScrapWebApp.Models;
using FluentValidation.Results;
using Azure;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Azure.Amqp.Framing;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
IConfiguration configuration = new ConfigurationBuilder()
       .SetBasePath(Directory.GetCurrentDirectory())
       .AddJsonFile("appsettings.json")
       .Build();

builder.Services.AddSingleton<ServiceBusClient>(serviceProvider =>
{
    string connectionString = configuration.GetConnectionString("ServiceBusConnection")!;
    return new ServiceBusClient(connectionString);
});
builder.Services.AddScoped<IValidator<LinkModel>, LinkModelValidator>();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var app = builder.Build();

//builder.Services.AddScoped<ConsoleAppService>();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.MapPost("/sendlink", async (HttpContext context, ServiceBusClient client, IConfiguration configuration, [FromBody] LinkModel linkModel, IValidator<LinkModel> validator) =>
{
    string sendQueueName = configuration.GetValue<string>("SendQueueName")!;
    string receiveQueueName = configuration.GetValue<string>("ReceiveQueueName")!;
       var partitionKey = Guid.NewGuid().ToString();  
    string linkedInProfileLink = linkModel.Link;
    await using ServiceBusSender sender = client.CreateSender(sendQueueName);
    // Create a Service Bus message with the LinkedIn profile link
    ServiceBusMessage message = new ServiceBusMessage(linkedInProfileLink)
    {
        PartitionKey = partitionKey
    };
    try
    {
        await sender.SendMessageAsync(message);
    } 
    catch (Exception)
    {
        throw;
    }
    ServiceBusReceivedMessage responseMessage = null!;
    await using (ServiceBusReceiver receiver = client.CreateReceiver(receiveQueueName))
    {
        while (responseMessage == null)
        {
            Console.WriteLine("printed");
            IEnumerable<ServiceBusReceivedMessage> receivedMessages = await receiver.ReceiveMessagesAsync(maxMessages: 100);

            if (receivedMessages.Any())
            {
                responseMessage = receivedMessages.FirstOrDefault(e=> e.PartitionKey == partitionKey)!;
                if (responseMessage != null)
                {
                    string response = responseMessage.Body.ToString();
                   await receiver.CompleteMessageAsync(responseMessage);
                    return response;
                }
            }            
        }
    }
    await context.Response.WriteAsJsonAsync(new { Data = "No matching response available" });
    return null;
});
app.Run();
