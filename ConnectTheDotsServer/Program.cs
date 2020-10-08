using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Web.Script.Serialization;
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace ConnectTheDotsServer
{
	public static class Program
	{
		private static readonly HttpListener server = new HttpListener();
		// ReSharper disable once NotAccessedField.Local
		private static Game game;

		// ReSharper disable once UnusedParameter.Global
		public static void Main(string[] args)
		{
			server.Prefixes.Add("http://localhost:8080/");
			server.Start();
			Console.WriteLine("The server has started.  Press <CTRL>-C to stop the server.");

			var context = server.GetContext();

			while (ProcessContext(context))
			{
				context = server.GetContext();
			}
		}

		private static bool ProcessContext(HttpListenerContext context)
		{
			var request = context.Request;

			switch (request.HttpMethod)
			{
				case "GET":
					{
						if (request.Url.AbsolutePath != "/initialize") return true;
						context.Response.AddHeader("Access-Control-Allow-Origin", "*"); // Needed to allow the client to read the response.
						game = new Game { CurrentPlayer = 1 };
						var payload = new Payload
						{
							msg = "INITIALIZE",
							body = new StateUpdate
							{
								newLine = null,
								heading = $"Player {game.CurrentPlayer}",
								message = $"Awaiting Player {game.CurrentPlayer}'s Move"
							}
						};

						SerializePayload(context, payload);
					}
					return true;

				case "OPTIONS":
					{
						context.Response.AddHeader("Access-Control-Allow-Origin", "*"); // Needed to allow the client to read the response.
						context.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
						context.Response.AddHeader("Access-Control-Max-Age", "86400");

						var jsSerializer = new JavaScriptSerializer();
						using var writer = new StreamWriter(context.Response.OutputStream);
						writer.Write("OK");
					}
					return true;

				case "POST":
					{
						context.Response.AddHeader("Access-Control-Allow-Origin", "*"); // Needed to allow the client to read the response.
						switch (request.Url.AbsolutePath)
						{
							case "/node-clicked":
								{
									var jsonDocument = ExtractJson(request);

									var node = new Point
									{
										x = jsonDocument.RootElement.GetProperty("x").GetInt32(),
										y = jsonDocument.RootElement.GetProperty("y").GetInt32()
									};

									if (game.LineNodes.Count == 0)
									{
										ProcessValidStartNode(context, node);
										return true;
									}

									if (game.NewSegmentStartNode == null)
									{
										if (node == game.LineNodes[0] ||
											node == game.LineNodes[game.LineNodes.Count - 1])
										{
											ProcessValidStartNode(context, node);
											return true;
										}

										ProcessInvalidStartNode(context);
										return true;
									}

									if (node == game.NewSegmentStartNode || game.InvalidNodes.Contains(node))
									{
										ProcessInvalidEndNode(context, node);
										return true;
									}

									for (var i = 1; i < game.LineNodes.Count - 1; i++)
									{
										if (game.NewSegmentStartNode == game.LineNodes[i - 1])
										{
											continue;
										}

										var existingStartNode = game.LineNodes[i - 1];
										var existingEndNode = game.LineNodes[i];


									}
								}
								return true;

							case "/error":
								{
									var jsonDocument = ExtractJson(request);
									Console.WriteLine(jsonDocument.RootElement.GetProperty("error").GetString());
								}
								return true;
						}

						break;
					}
			}
			return true;
		}

		private static void ProcessValidEndNode(HttpListenerContext context, Point node)
		{
			game.CurrentPlayer++;
			if (game.CurrentPlayer > 2)
			{
				game.CurrentPlayer = 1;
			}

			game.LineNodes.Add(game.NewSegmentStartNode);
			game.LineNodes.Add(node);

			var x = game.NewSegmentStartNode!.x;
			var y = game.NewSegmentStartNode!.y;
			var xInc = 0;
			var yInc = 0;

			if (x < node.x)
			{
				xInc = 1;
			}
			else
			{
				if (x > node.x)
				{
					xInc = -1;
				}
			}

			if (y < node.y)
			{
				yInc = 1;
			}
			else
			{
				if (y > node.y)
				{
					yInc = -1;
				}
			}

			while (x != node.x && y != node.y)
			{
				game.InvalidNodes.Add(new Point { x = x, y = y });
				x += xInc;
				y += yInc;
			}

			game.NewSegmentStartNode = null;

			var payload = new Payload
			{
				msg = "VALID_END_NODE",
				body = new StateUpdate
				{
					newLine = new Line
					{
						start = game.NewSegmentStartNode,
						end = node
					},
					heading = $"Player {game.CurrentPlayer}",
					message = $"Awaiting Player {game.CurrentPlayer}'s Move"
				}
			};
		}

		private static void ProcessInvalidStartNode(HttpListenerContext context)
		{
			var payload = new Payload
			{
				msg = "INVALID_START_NODE",
				body = new StateUpdate
				{
					newLine = null,
					heading = $"Player {game.CurrentPlayer}",
					message = "Not a valid starting position."
				}
			};

			SerializePayload(context, payload);
		}

		private static void ProcessValidStartNode(HttpListenerContext context, Point node)
		{
			game.NewSegmentStartNode = node;

			var payload = new Payload
			{
				msg = "VALID_START_NODE",
				body = new StateUpdate
				{
					newLine = null,
					heading = $"Player {game.CurrentPlayer}",
					message = "Select a second node to complete the line."
				}
			};

			SerializePayload(context, payload);
		}

		private static void ProcessInvalidEndNode(HttpListenerContext context, Point node)
		{
			game.NewSegmentStartNode = null;

			var payload = new Payload
			{
				msg = "INVALID_END_NODE",
				body = new StateUpdate
				{
					newLine = null,
					heading = $"Player {game.CurrentPlayer}",
					message = "Invalid move.  Please try again."
				}
			};

			SerializePayload(context, payload);
		}

		private static JsonDocument ExtractJson(HttpListenerRequest request)
		{
			var requestPayload = "";
			using (var reader = new StreamReader(request.InputStream))
			{
				requestPayload = reader.ReadToEnd();
			}

			return JsonDocument.Parse(requestPayload);
		}

		private static void SerializePayload(HttpListenerContext context, Payload payload)
		{
			var jsSerializer = new JavaScriptSerializer();

			using var writer = new StreamWriter(context.Response.OutputStream);
			writer.Write(jsSerializer.Serialize(payload));
		}

		private class Point
		{
			public int x { get; set; }
			public int y { get; set; }
		}

		private class Line
		{
			public Point start { get; set; }
			public Point end { get; set; }
		}

		private class Game
		{
			public List<Point> LineNodes { get; } = new List<Point>();
			public int CurrentPlayer { get; set; }
			public Point? NewSegmentStartNode { get; set; }
			public List<Point> InvalidNodes { get; } = new List<Point>();
		}

		private class Payload
		{
			public string msg { get; set; }
			public object body { get; set; }
		}

		private class StateUpdate
		{
			public Line newLine { get; set; }
			public string heading { get; set; }
			public string message { get; set; }
		}
	}
}