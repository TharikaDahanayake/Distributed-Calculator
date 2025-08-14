
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Json;
using Grpc.Net.Client;
using Shared;
using CalculatorServer;
using Serilog;

class Program
{
	static async Task Main(string[] args)
	{
		Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

		Log.Information("Starting Calculator Client...");
		Log.Information("Select mode:");
		Log.Information("1: Interactive Mode");
		Log.Information("2: Parallel Test Mode");
		Log.Information("3: Clock Sync Test Mode");
		Log.Information("4: Two-Phase Commit Test");
		
		var mode = Console.ReadLine()?.Trim();
		if (mode == "2")
		{
			await RunParallelTest();
			return;
		}
		else if (mode == "3")
		{
			await RunClockSyncTest();
			return;
		}
		else if (mode == "4")
		{
			await RunTwoPhaseCommitTest();
			return;
		}

		// List of available servers
		var servers = new List<string> { "https://localhost:5001", "https://localhost:5002" };
		
		// Initialize server manager with leader election support
		var serverManager = new ServerManager(servers);
		Log.Information("Current leader server: {Leader}", serverManager.GetCurrentLeader());

		// Initialize Lamport clock
		var lamportClock = new LamportClock();
		var clientId = $"Client_{Guid.NewGuid().ToString().Substring(0, 4)}";

		while (true)
		{
			Log.Information("Enter a number (or 'exit' to quit): ");
			var input = Console.ReadLine();
			if (input?.Trim().ToLower() == "exit") break;
			if (!int.TryParse(input, out int number))
			{
				Log.Error("Invalid number.");
				continue;
			}

			Log.Information("Operation (square/cube): ");
			var op = Console.ReadLine()?.Trim().ToLower();
			if (op != "square" && op != "cube")
			{
				Log.Error("Invalid operation.");
				continue;
			}

			// Increment clock before sending request
			lamportClock.Increment();

			// Prepare request
			var req = new CalculationRequest
			{
				Number = number,
				Timestamp = lamportClock.GetTime()
			};

			// Call server through ServerManager (handles leader election and failover)
			CalculationResponse resp = null!;
			try
			{
				resp = await serverManager.ExecuteOperation(number, op, req.Timestamp);
				
				// Update clock with received timestamp
				lamportClock.UpdateOnReceive(resp.Timestamp);
				
				Log.Information("Operation executed by leader: {Leader}", serverManager.GetCurrentLeader());
			}
			catch (Exception ex)
			{
				Log.Error("Operation failed: {Error}", ex.Message);
				continue;
			}

			// Log Lamport clock evolution
			Log.Information("[LamportClock] After {Op}: {Clock}", op, lamportClock.GetTime());

			// Post clock value to dashboard
			var clockDict = new Dictionary<string, int> { { clientId, lamportClock.GetTime() } };
			await PostVectorClockAsync(clientId, clockDict);

			// Show result
			if (resp.IsSuccess)
				Log.Information("Result: {Result}", resp.Result);
			else
				Log.Error("Error: {Message}", resp.Message);
		}

		// Method to POST vector clock to dashboard
		static async Task PostVectorClockAsync(string nodeId, Dictionary<string, int> clock)
		{
			try
			{
				using var http = new HttpClient();
				var update = new { NodeId = nodeId, Clock = clock };
				await http.PostAsJsonAsync("http://localhost:5005/api/clock", update);
			}
			catch (Exception ex)
			{
				Log.Error("[Dashboard] Failed to post vector clock: {Error}", ex.Message);
			}
		}
	Log.CloseAndFlush();
	}

	static async Task RunParallelTest()
	{
		var serverAddress = "https://localhost:5001";
		using var channel = GrpcChannel.ForAddress(serverAddress);
		var client = new CalculatorService.CalculatorServiceClient(channel);

		// Initialize Lamport clock
		var lamportClock = new LamportClock();
		var clientId = $"Client_{Guid.NewGuid().ToString().Substring(0, 4)}";

		// Create a list of parallel operations
		var operations = new List<(int number, string op)>
		{
			(5, "square"),   // Operation 1
			(3, "cube"),     // Operation 2
			(4, "square"),   // Operation 3
			(2, "cube"),     // Operation 4
		};

		Log.Information("Starting parallel operations with Lamport Clock...");
		Log.Information("Initial Lamport time: {Time}", lamportClock.GetTime());

		// Run operations in parallel
		var tasks = operations.Select(async op =>
		{
			// Simulate random delay before operation (0-1000ms)
			await Task.Delay(Random.Shared.Next(1000));
			
			// Increment clock before sending request
			lamportClock.Increment();
			var requestTime = lamportClock.GetTime();

			var req = new CalculationRequest
			{
				Number = op.number,
				Timestamp = requestTime
			};

			try
			{
				CalculationResponse resp;
				if (op.op == "square")
					resp = await client.SquareAsync(req);
				else
					resp = await client.CubeAsync(req);

				// Update clock with received timestamp
				lamportClock.UpdateOnReceive(resp.Timestamp);

				Log.Information(
					"Operation: {Op}({Number}) | Request Time: {ReqTime} | Response Time: {RespTime} | Result: {Result}",
					op.op,
					op.number,
					requestTime,
					resp.Timestamp,
					resp.Result
				);
			}
			catch (Exception ex)
			{
				Log.Error("Operation {Op}({Number}) failed: {Error}", op.op, op.number, ex.Message);
			}
		}).ToList();

		// Wait for all operations to complete
		await Task.WhenAll(tasks);

		Log.Information("All parallel operations completed.");
		Log.Information("Final Lamport time: {Time}", lamportClock.GetTime());
		Log.Information("\nKey observations about Lamport Clocks:");
		Log.Information("1. Each operation gets a monotonically increasing timestamp");
		Log.Information("2. The timestamps create a total ordering of events");
		Log.Information("3. However, we cannot determine if operations were truly concurrent");
		Log.Information("4. The final time represents the latest known event in the system");
	}

	static async Task RunClockSyncTest()
	{
		// Create clients for both servers
		var servers = new List<string> { "https://localhost:5001", "https://localhost:5002" };
		var handler = new HttpClientHandler
		{
			ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
		};

		var channels = servers.Select(server => GrpcChannel.ForAddress(server, new GrpcChannelOptions
		{
			HttpHandler = handler,
			ThrowOperationCanceledOnCancellation = true
		})).ToList();

		var clients = channels.Select(channel => 
			new CalculatorService.CalculatorServiceClient(channel)).ToList();

		Log.Information("Starting Clock Synchronization Test...");
		
		// Initialize clocks for tracking server states
		var server1Clock = 0;
		var server2Clock = 0;

		// Function to check clock divergence
		void CheckDivergence()
		{
			var difference = Math.Abs(server1Clock - server2Clock);
			if (difference > 5)
			{
				Log.Warning("DIVERGED: Clock difference is {Difference} units", difference);
			}
		}

		// Run a series of operations on both servers with delays
		for (int i = 0; i < 5; i++)
		{
			Log.Information("\nIteration {Iteration}:", i + 1);
			
			// Simulate some work on Server 1
			try
			{
				var req1 = new CalculationRequest { Number = 2, Timestamp = server1Clock };
				var resp1 = await clients[0].SquareAsync(req1);
				server1Clock = resp1.Timestamp;
				Log.Information("Server 1 Clock: {Clock}", server1Clock);
			}
			catch (Exception ex)
			{
				Log.Error("Server 1 error: {Error}", ex.Message);
			}

			// Add delay to simulate network latency
			await Task.Delay(Random.Shared.Next(1000, 3000));

			// Simulate some work on Server 2
			try
			{
				var req2 = new CalculationRequest { Number = 3, Timestamp = server2Clock };
				var resp2 = await clients[1].CubeAsync(req2);
				server2Clock = resp2.Timestamp;
				Log.Information("Server 2 Clock: {Clock}", server2Clock);
			}
			catch (Exception ex)
			{
				Log.Error("Server 2 error: {Error}", ex.Message);
			}

			// Check for clock divergence
			CheckDivergence();

			// Simulate sync delay
			await Task.Delay(2000);

			// Sync clocks by getting the maximum time
			var maxClock = Math.Max(server1Clock, server2Clock);
			server1Clock = maxClock;
			server2Clock = maxClock;
			Log.Information("After sync - Both clocks: {Clock}", maxClock);
		}

		Log.Information("\nClock Synchronization Test Completed");
		foreach (var channel in channels)
		{
			await channel.ShutdownAsync();
		}
	}

	static async Task RunTwoPhaseCommitTest()
	{
		var servers = new List<string> { "https://localhost:5001", "https://localhost:5002" };
		var handler = new HttpClientHandler
		{
			ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
		};

		// Create channels for both servers
		var channels = servers.Select(server => GrpcChannel.ForAddress(server, new GrpcChannelOptions
		{
			HttpHandler = handler,
			ThrowOperationCanceledOnCancellation = true
		})).ToList();

		var clients = channels.Select(channel => 
			new CalculatorService.CalculatorServiceClient(channel)).ToList();

		// Initialize Lamport clock
		var lamportClock = new LamportClock();

		while (true)
		{
			try
			{
				Log.Information("\nEnter a number for distributed calculation (or 'exit' to quit): ");
				var input = Console.ReadLine();
				if (input?.Trim().ToLower() == "exit") break;
				if (!int.TryParse(input, out int number))
				{
					Log.Error("Invalid number.");
					continue;
				}

				// Phase 1: PREPARE - Check if both servers are ready
				Log.Information("Phase 1: PREPARE - Checking server availability...");
				
				var prepareResults = new Dictionary<string, bool>();
				const int maxRetries = 3;

				for (int i = 0; i < clients.Count; i++)
				{
					var serverUrl = servers[i];
					var retryCount = 0;
					var success = false;

					while (retryCount < maxRetries && !success)
					{
						try
						{
							if (retryCount > 0)
							{
								Log.Information("Retry attempt {Attempt} for server {Server}...", 
									retryCount + 1, serverUrl);
								await Task.Delay(1000 * retryCount); // Exponential backoff
							}
							else
							{
								Log.Information("Checking server at {Server}...", serverUrl);
							}

							lamportClock.Increment();
							
							// Simple health check with a test calculation
							var healthResp = await clients[i].SquareAsync(new CalculationRequest 
							{ 
								Number = 1,
								Timestamp = lamportClock.GetTime()
							});
							lamportClock.UpdateOnReceive(healthResp.Timestamp);
							
							Log.Information("Server {Server} health check: {Status}", serverUrl, healthResp.IsSuccess);
							prepareResults[serverUrl] = healthResp.IsSuccess;
							success = true;
						}
						catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
						{
							Log.Warning("Server {Server} is unavailable (attempt {Attempt}/{MaxRetries})", 
								serverUrl, retryCount + 1, maxRetries);
							retryCount++;
							
							if (retryCount >= maxRetries)
							{
								Log.Error("Server {Server} failed all retry attempts - Status: {Status}", 
									serverUrl, ex.StatusCode);
								prepareResults[serverUrl] = false;
							}
						}
						catch (Grpc.Core.RpcException ex)
						{
							Log.Error("Server {Server} failed with RPC error: {Status} - {Message}", 
								serverUrl, ex.StatusCode, ex.Message);
							prepareResults[serverUrl] = false;
							break;
						}
						catch (Exception ex)
						{
							Log.Error("Server {Server} failed with unexpected error: {Error}", 
								serverUrl, ex.Message);
							prepareResults[serverUrl] = false;
							break;
						}
					}
				}

				// If any server is not ready, abort
				var failedServers = prepareResults.Where(r => !r.Value).ToList();
				if (failedServers.Any())
				{
					Log.Warning("Phase 1 failed - The following servers are not ready:");
					foreach (var server in failedServers)
					{
						Log.Warning("  - {Server}", server.Key);
					}
					Log.Warning("Aborting transaction.");
					continue;
				}

				Log.Information("All servers ready. Proceeding with calculation...");

				// Phase 2: EXECUTE - Perform the distributed calculation
				Log.Information("Phase 2: EXECUTE - Running distributed calculation...");
				
				try
				{
					// First server computes square
					lamportClock.Increment();
					var squareResp = await clients[0].SquareAsync(new CalculationRequest 
					{ 
						Number = number,
						Timestamp = lamportClock.GetTime()
					});
					lamportClock.UpdateOnReceive(squareResp.Timestamp);
					
					if (!squareResp.IsSuccess)
					{
						throw new Exception($"Square calculation failed: {squareResp.Message}");
					}

					Log.Information("Square calculation successful: {Number}² = {Result}", 
						number, squareResp.Result);

					// Second server computes cube of the result
					lamportClock.Increment();
					var cubeResp = await clients[1].CubeAsync(new CalculationRequest 
					{ 
						Number = (int)squareResp.Result,
						Timestamp = lamportClock.GetTime()
					});
					lamportClock.UpdateOnReceive(cubeResp.Timestamp);

					if (!cubeResp.IsSuccess)
					{
						throw new Exception($"Cube calculation failed: {cubeResp.Message}");
					}

					Log.Information("Cube calculation successful: {Number}³ = {Result}", 
						squareResp.Result, cubeResp.Result);

					// Transaction completed successfully
					Log.Information("\nDistributed calculation completed!");
					Log.Information("Full calculation: ({0})² = {1}, then {1}³ = {2}", 
						number, squareResp.Result, cubeResp.Result);
					Log.Information("Final Lamport Clock: {Time}", lamportClock.GetTime());
				}
				catch (Exception ex)
				{
					Log.Error("Transaction failed during execution: {Error}", ex.Message);
					Log.Warning("Rolling back changes (if any)...");
					
					// In a real system, we would implement rollback logic here
					// For this example, we just log the rollback attempt
					foreach (var client in clients)
					{
						try
						{
							lamportClock.Increment();
							await client.SquareAsync(new CalculationRequest 
							{ 
								Number = 0, // Dummy rollback request
								Timestamp = lamportClock.GetTime()
							});
						}
						catch (Exception rollbackEx)
						{
							Log.Error("Error during rollback: {Error}", rollbackEx.Message);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("Unexpected error: {Error}", ex.Message);
			}
		}

		// Cleanup
		foreach (var channel in channels)
		{
			await channel.ShutdownAsync();
		}
	}
}
