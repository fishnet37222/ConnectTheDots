#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
		private static Game game = new Game();
		private const int gridWidth = 4;
		private const int gridHeight = 4;

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
									(
										jsonDocument.RootElement.GetProperty("x").GetInt32(),
										jsonDocument.RootElement.GetProperty("y").GetInt32()
									);

									if (game.LineNodes.Count == 0 && game.NewSegmentStartNode == null)
									{
										ProcessValidStartNode(context, node);
										return true;
									}

									if (game.NewSegmentStartNode == null)
									{
										if (node.Equals(game.LineNodes[0]) ||
											node.Equals(game.LineNodes[game.LineNodes.Count - 1]))
										{
											ProcessValidStartNode(context, node);
											return true;
										}

										ProcessInvalidStartNode(context);
										return true;
									}

									if (node.Equals(game.NewSegmentStartNode) || game.InvalidNodes.Contains(node))
									{
										ProcessInvalidEndNode(context);
										return true;
									}

									for (var i = 1; i < game.LineNodes.Count - 1; i++)
									{
										if (game.NewSegmentStartNode.Equals(game.LineNodes[i - 1]))
										{
											continue;
										}

										if (i == game.LineNodes.Count - 1)
										{
											if (game.NewSegmentStartNode.Equals(game.LineNodes[i]))
											{
												continue;
											}
										}

										var existingStartNode = game.LineNodes[i - 1];
										var existingEndNode = game.LineNodes[i];

										if (!DoIntersect(existingStartNode, existingEndNode, game.NewSegmentStartNode, node)) continue;
										ProcessInvalidEndNode(context);
										return true;
									}

									ProcessValidEndNode(context, node);
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

			if (game.LineNodes.Count == 0)
			{
				game.LineNodes.Add(game.NewSegmentStartNode!);
				game.LineNodes.Add(node);
			}
			else
			{
				if (game.NewSegmentStartNode!.Equals(game.LineNodes[0]))
				{
					game.LineNodes.Insert(0, node);
				}
				else
				{
					game.LineNodes.Add(node);
				}
			}

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

			while (x != node.x || y != node.y)
			{
				game.InvalidNodes.Add(new Point(x, y));
				x += xInc;
				y += yInc;
			}

			game.InvalidNodes.Add(node);

			if (!ValidMovesLeft())
			{
				var payload = new Payload
				{
					msg = "GAME_OVER",
					body = new StateUpdate
					{
						newLine = new Line
						{
							start = game.NewSegmentStartNode,
							end = node
						},
						heading = "Game Over",
						message = $"Player {game.CurrentPlayer} Wins!"
					}
				};

				SerializePayload(context, payload);
			}
			else
			{
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

				SerializePayload(context, payload);
				game.NewSegmentStartNode = null;
			}
		}

		private static bool ValidMovesLeft()
		{
			var possibleEndNodes = new List<Point>();
			for (var y = 0; y < gridHeight; y++)
			{
				for (var x = 0; x < gridWidth; x++)
				{
					var newNode = new Point(x, y);
					var result = (from node in game.InvalidNodes
								  where node.Equals(newNode)
								  select node).ToList();
					if (result.Count == 0)
					{
						possibleEndNodes.Add(newNode);
					}
				}
			}

			if (possibleEndNodes.Count == 0)
			{
				return false;
			}

			var possibleStartNode = game.LineNodes[0];
			if (!CheckIntersects(possibleEndNodes, possibleStartNode)) return true;

			possibleStartNode = game.LineNodes[game.LineNodes.Count - 1];
			return !CheckIntersects(possibleEndNodes, possibleStartNode);
		}

		private static bool CheckIntersects(IEnumerable<Point> possibleEndNodes, Point possibleStartNode)
		{
			foreach (var possibleEndNode in possibleEndNodes)
			{
				var possibleLineIntersects = false;
				for (var i = 1; i < game.LineNodes.Count - 1; i++)
				{
					if (possibleStartNode.Equals(game.LineNodes[i - 1]))
					{
						continue;
					}

					if (i == game.LineNodes.Count - 1)
					{
						if (possibleStartNode.Equals(game.LineNodes[i]))
						{
							continue;
						}
					}

					var existingStartNode = game.LineNodes[i - 1];
					var existingEndNode = game.LineNodes[i];

					if (!DoIntersect(existingStartNode, existingEndNode, possibleStartNode, possibleEndNode)) continue;
					possibleLineIntersects = true;
					break;
				}

				if (!possibleLineIntersects)
				{
					return false;
				}
			}

			return true;
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

		private static void ProcessInvalidEndNode(HttpListenerContext context)
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
			string requestPayload;
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

		// Given three co-linear points p, q, r, the function checks if 
		// point q lies on line segment 'pr' 
		private static bool OnSegment(Point p, Point q, Point r)
		{
			return q.x <= Math.Max(p.x, r.x) && q.x >= Math.Min(p.x, r.x) &&
				   q.y <= Math.Max(p.y, r.y) && q.y >= Math.Min(p.y, r.y);
		}
		// To find orientation of ordered triplet (p, q, r). 
		// The function returns following values 
		// 0 --> p, q and r are co-linear 
		// 1 --> Clockwise 
		// 2 --> Counterclockwise 
		private static int Orientation(Point p, Point q, Point r)
		{
			// See https://www.geeksforgeeks.org/orientation-3-ordered-points/ 
			// for details of below formula. 
			var val = (q.y - p.y) * (r.x - q.x) -
					  (q.x - p.x) * (r.y - q.y);

			if (val == 0) return 0; // co-linear 

			return (val > 0) ? 1 : 2; // clock or counterclockwise 
		}

		// The main function that returns true if line segment 'p1q1' 
		// and 'p2q2' intersect. 
		private static bool DoIntersect(Point p1, Point q1, Point p2, Point q2)
		{
			// Find the four orientations needed for general and 
			// special cases 
			var o1 = Orientation(p1, q1, p2);
			var o2 = Orientation(p1, q1, q2);
			var o3 = Orientation(p2, q2, p1);
			var o4 = Orientation(p2, q2, q1);

			// General case 
			if (o1 != o2 && o3 != o4)
				return true;

			// Special Cases 
			// p1, q1 and p2 are co-linear and p2 lies on segment p1q1 
			if (o1 == 0 && OnSegment(p1, p2, q1)) return true;

			// p1, q1 and q2 are co-linear and q2 lies on segment p1q1 
			if (o2 == 0 && OnSegment(p1, q2, q1)) return true;

			// p2, q2 and p1 are co-linear and p1 lies on segment p2q2 
			if (o3 == 0 && OnSegment(p2, p1, q2)) return true;

			// p2, q2 and q1 are co-linear and q1 lies on segment p2q2 
			if (o4 == 0 && OnSegment(p2, q1, q2)) return true;

			return false; // Doesn't fall in any of the above cases 
		}
		private class Point
		{
			public int x { get; }
			public int y { get; }

			public Point(int x, int y)
			{
				this.x = x;
				this.y = y;
			}

			public override bool Equals(object obj)
			{
				if (obj is Point p)
				{
					return p.x == x && p.y == y;
				}

				return false;
			}

			// ReSharper disable once UnusedMember.Local
			protected bool Equals(Point other)
			{
				return x == other.x && y == other.y;
			}

			public override int GetHashCode()
			{
				unchecked
				{
					return (x * 397) ^ y;
				}
			}
		}

		private class Line
		{
			public Point? start { get; set; }
			public Point? end { get; set; }
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
			public string msg { get; set; } = "";
			public object? body { get; set; }
		}

		private class StateUpdate
		{
			public Line? newLine { get; set; }
			public string? heading { get; set; }
			public string? message { get; set; }
		}
	}
}