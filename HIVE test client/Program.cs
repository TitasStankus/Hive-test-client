using Google.FlatBuffers;
using System.Net.Sockets;
using HIVE.Commons.Flatbuffers.Generated;


try
{
    string serverIP = "127.0.0.1";
    int port = 6000;
    using (TcpClient client = new TcpClient(serverIP, port))
    {
        Console.WriteLine($"Connected to data center at {serverIP}:{port}");
        using (NetworkStream stream = client.GetStream())
        {
            // Send the magic number first (4 bytes, e.g., 0x23476945)
            uint magic = 0x23476945;
            byte[] magicBytes = BitConverter.GetBytes(magic);
            stream.Write(magicBytes, 0, magicBytes.Length);

            // Build and send the robot request
            var robotRequestBuilder = new FlatBufferBuilder(256);

            // Add required string fields
            var nameOffset = robotRequestBuilder.CreateString("testRobot");

            // Build a bounding box (example values, adjust as needed)
            var centreT = new Vec3T { X = 0, Y = 0, Z = 0 };
            var dimensionsT = new Vec3T { X = 0, Y = 0, Z = 0 };
            var rotationT = new Vec4T { W = 0, X = 0, Y = 0, Z = 1 };

            var boundingBoxOffset = BoundingBox.CreateBoundingBox(
                robotRequestBuilder,
                centreT,
                dimensionsT,
                rotationT,
                false
            );

            // Build the robot
            Robot.StartRobot(robotRequestBuilder);
            Robot.AddId(robotRequestBuilder, 123UL);
            Robot.AddName(robotRequestBuilder, nameOffset);
            Robot.AddSubscription(robotRequestBuilder, (ushort)20);
            Robot.AddRate(robotRequestBuilder, SubscriptionRate.Full);
            Robot.AddBoundingBox(robotRequestBuilder, boundingBoxOffset);
            Robot.AddColour(robotRequestBuilder, 0xFF0000FF);
            var robotOffset = Robot.EndRobot(robotRequestBuilder);

            var entityOffset = Entity.CreateEntity(robotRequestBuilder, EntityUnion.Robot, robotOffset.Value);
            robotRequestBuilder.Finish(entityOffset.Value);
            byte[] robotRequestBytes = robotRequestBuilder.SizedByteArray();

            // Create outer flatbuffer
            var builder = new FlatBufferBuilder(1024);
            var dataVector = Payload.CreateDataVector(builder, robotRequestBytes);
            // Create inner flatbuffer
            Payload.StartPayload(builder);
            Payload.AddData(builder, dataVector);
            var payloadOffset = Payload.EndPayload(builder);

            var payloadsVector = State.CreatePayloadVector(builder, new[] { payloadOffset });

            // Finally create the state that contains the payload
            State.StartState(builder);
            State.AddPayload(builder, payloadsVector);
            var stateOffset = State.EndState(builder);

            builder.Finish(stateOffset.Value);
            // Instead of sending robotRequestBytes directly, send the full State FlatBuffer message
            byte[] stateBytes = builder.SizedByteArray();

            // Send length prefix for the state message
            byte[] stateLenPrefix = BitConverter.GetBytes(stateBytes.Length);
            stream.Write(stateLenPrefix, 0, stateLenPrefix.Length);

            // Send the actual state message
            stream.Write(stateBytes, 0, stateBytes.Length);

            Console.WriteLine("");
            Console.WriteLine($"Sent {stateBytes.Length} bytes to server");
            Thread.Sleep(100);

            // Wait for server to process
            Console.WriteLine("");
            Console.WriteLine("Waiting for server response...");
            Thread.Sleep(2000); // Wait 2 seconds for processing

            // Check if data is available first
            if (stream.DataAvailable)
            {
                // Read length prefix first (4 bytes)
                byte[] lengthBuffer = new byte[4];
                int lengthBytesRead = 0;
                while (lengthBytesRead < 4)
                {
                    lengthBytesRead += stream.Read(lengthBuffer, lengthBytesRead, 4 - lengthBytesRead);
                }

                int responseLength = BitConverter.ToInt32(lengthBuffer, 0);
                Console.WriteLine("");
                Console.WriteLine($"Server response length: {responseLength} bytes");

                // Read the actual data
                byte[] responseData = new byte[responseLength];
                int totalBytesRead = 0;

                while (totalBytesRead < responseLength)
                {
                    int bytesRead = stream.Read(responseData, totalBytesRead, responseLength - totalBytesRead);
                    if (bytesRead == 0) break; // Connection closed
                    totalBytesRead += bytesRead;
                    Console.WriteLine("");
                    Console.WriteLine($"Read {bytesRead} bytes (total: {totalBytesRead}/{responseLength})");
                }
                // Show how many bytes were received
                Console.WriteLine("");
                Console.WriteLine($"Received {totalBytesRead} bytes from server");

                // DEBUG: Print the raw bytes in a readable format
                Console.WriteLine("");
                Console.WriteLine("Raw Response Data");
                Console.WriteLine($"Hex: {BitConverter.ToString(responseData, 0, Math.Min(totalBytesRead, 100))}..."); // First 100 bytes
                Console.WriteLine($"As ASCII: {System.Text.Encoding.ASCII.GetString(responseData, 0, Math.Min(totalBytesRead, 100))}...");

                try
                {
                    // Try to parse as State first (your expected format)
                    var byteBuffer = new ByteBuffer(responseData);
                    var state = State.GetRootAsState(byteBuffer);

                    Console.WriteLine("");
                    Console.WriteLine($"Successfully parsed as State with {state.PayloadLength} payload(s)");

                    for (int i = 0; i < state.PayloadLength; i++)
                    {
                        var payload = state.Payload(i);
                        if (payload.HasValue)
                        {
                            var dataSegment = payload.Value.GetDataBytes();
                            if (dataSegment.HasValue)
                            {
                                var segment = dataSegment.Value;
                                byte[] flatBufferBytes = segment.Array != null
                                    ? segment.Array.Skip(segment.Offset).Take(segment.Count).ToArray()
                                    : Array.Empty<byte>();

                                Console.WriteLine("");
                                Console.WriteLine($"Payload {i + 1}");
                                Console.WriteLine($"Payload size: {flatBufferBytes.Length} bytes");
                                Console.WriteLine($"Hex: {BitConverter.ToString(flatBufferBytes)}");
                                Console.WriteLine($"As string: {System.Text.Encoding.UTF8.GetString(flatBufferBytes)}");

                                // Try to parse the payload as Entity (your robot data)
                                try
                                {
                                    var entityBuffer = new ByteBuffer(flatBufferBytes);
                                    var entity = Entity.GetRootAsEntity(entityBuffer);

                                    Console.WriteLine($"Entity type: {entity.EntityType}");

                                    if (entity.EntityType == EntityUnion.Robot)
                                    {
                                        var robot = entity.Entity_AsRobot();
                                        Console.WriteLine("");
                                        Console.WriteLine("Robot Data Found");
                                        Console.WriteLine($"Robot ID: {robot.Id}");
                                        Console.WriteLine($"Robot Name: {robot.Name}");
                                        Console.WriteLine($"Subscription: {robot.Subscription}");
                                        Console.WriteLine($"Rate: {robot.Rate}");

                                        // Check and output bounding box values
                                        var boundingBox = robot.BoundingBox;
                                        if (boundingBox.HasValue)
                                        {
                                            var bb = boundingBox.Value;
                                            Console.WriteLine("");
                                            Console.WriteLine("Bounding Box Details:");

                                            // Centre
                                            var centre = bb.Centre;
                                            if (centre.HasValue)
                                            {
                                                var c = centre.Value;
                                                Console.WriteLine($"  Centre: X={c.X:F3}, Y={c.Y:F3}, Z={c.Z:F3}");
                                            }
                                            else
                                            {
                                                Console.WriteLine("  Centre: null");
                                            }

                                            // Dimensions
                                            var dimensions = bb.Dimensions;
                                            if (dimensions.HasValue)
                                            {
                                                var d = dimensions.Value;
                                                Console.WriteLine($"  Dimensions: X={d.X:F3}, Y={d.Y:F3}, Z={d.Z:F3}");
                                            }
                                            else
                                            {
                                                Console.WriteLine("  Dimensions: null");
                                            }

                                            // Rotation
                                            var rotation = bb.Rotation;
                                            if (rotation.HasValue)
                                            {
                                                var r = rotation.Value;
                                                Console.WriteLine($"  Rotation: W={r.W:F3}, X={r.X:F3}, Y={r.Y:F3}, Z={r.Z:F3}");
                                            }
                                            else
                                            {
                                                Console.WriteLine("  Rotation: null");
                                            }

                                            // Ellipsoid flag
                                            Console.WriteLine($"  Ellipsoid: {bb.Ellipsoid}");

                                            // Draw a square with robot
                                            var position = dimensions.Value;
                                            float x = position.X;
                                            float y = position.Y;

                                            for (int a = 0; a < 100; a++)
                                            {
                                                x = x + 0.1f;
                                                dimensionsT.X = x;
                                                SendRobotUpdate(client, x, y);

                                            }
                                            for (int b = 0; b < 100; b++)
                                            {
                                                y = y + 0.1f;
                                                dimensionsT.Y = y;
                                                SendRobotUpdate(client, x, y);
                                            }
                                            for (int a = 0; a < 100; a++)
                                            {
                                                x = x - 0.1f;
                                                dimensionsT.X = x;
                                                SendRobotUpdate(client, x, y);
                                            }
                                            for (int b = 0; b < 100; b++)
                                            {
                                                y = y - 0.1f;
                                                dimensionsT.Y = y;
                                                SendRobotUpdate(client, x, y);
                                            }

                                        }
                                        else
                                        {
                                            Console.WriteLine("Bounding Box: null or not present");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("");
                                        Console.WriteLine($"Entity is not a Robot, it's: {entity.EntityType}");
                                    }
                                }
                                catch (Exception entityEx)
                                {
                                    Console.WriteLine("");
                                    Console.WriteLine($"Could not parse payload as Entity: {entityEx.Message}");

                                    // Try other possible formats
                                    Console.WriteLine("");
                                    Console.WriteLine("Trying alternative parsing...");

                                    // Maybe it's a direct Robot without Entity wrapper?
                                    try
                                    {
                                        var robotBuffer = new ByteBuffer(flatBufferBytes);
                                        var robot = Robot.GetRootAsRobot(robotBuffer);
                                        Console.WriteLine("");
                                        Console.WriteLine("Found Direct Robot Data");
                                        Console.WriteLine($"Robot ID: {robot.Id}");
                                        Console.WriteLine($"Robot Name: {robot.Name}");
                                    }
                                    catch
                                    {
                                        Console.WriteLine("");
                                        Console.WriteLine("Not a direct Robot either");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("");
                    Console.WriteLine($"Failed to parse as State: {ex.Message}");

                    // Try parsing as direct Entity
                    try
                    {
                        var entityBuffer = new ByteBuffer(responseData);
                        var entity = Entity.GetRootAsEntity(entityBuffer);
                        Console.WriteLine("");
                        Console.WriteLine("Response is a direct Entity");
                        Console.WriteLine($"Entity type: {entity.EntityType}");

                        if (entity.EntityType == EntityUnion.Robot)
                        {
                            var robot = entity.Entity_AsRobot();
                            Console.WriteLine("");
                            Console.WriteLine($"Robot ID: {robot.Id}");
                            Console.WriteLine($"Robot Name: {robot.Name}");
                        }
                    }
                    catch (Exception entityEx)
                    {
                        Console.WriteLine("");
                        Console.WriteLine($"Not a direct Entity either: {entityEx.Message}");
                    }

                    // Try parsing as direct Robot
                    try
                    {
                        var robotBuffer = new ByteBuffer(responseData);
                        var robot = Robot.GetRootAsRobot(robotBuffer);
                        Console.WriteLine("");
                        Console.WriteLine("Response is a direct Robot");
                        Console.WriteLine($"Robot ID: {robot.Id}");
                        Console.WriteLine($"Robot Name: {robot.Name}");
                    }
                    catch (Exception robotEx)
                    {
                        Console.WriteLine("");
                        Console.WriteLine($"Not a direct Robot either: {robotEx.Message}");
                    }

                    // Final fallback - just show what we got
                    Console.WriteLine("");
                    Console.WriteLine("FINAL RAW DATA");
                    string fullText = System.Text.Encoding.UTF8.GetString(responseData, 0, totalBytesRead);
                    Console.WriteLine($"Full response as text: {fullText}");
                }


                System.Timers.Timer pulseTimer = new System.Timers.Timer(1000);
                pulseTimer.Elapsed += (sender, e) => SendPulse(client);
                pulseTimer.Start();

                Console.WriteLine("");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();

                pulseTimer.Stop();
                pulseTimer.Dispose();
            }
            else
            {
                Console.WriteLine("");
                Console.WriteLine("No data available from server yet - it might still be processing");
                // Try increasing the wait time or check if your robot data format is correct
            }

            void SendPulse(TcpClient client)
            {
                try
                {
                    if (client.Connected)
                    {
                        NetworkStream stream = client.GetStream();
                        if (stream.CanWrite)
                        {
                            uint pulseMagic = 0xDEADBEEF;
                            byte[] pulseBytes = BitConverter.GetBytes(pulseMagic);

                            stream.Write(pulseBytes, 0, pulseBytes.Length);
                            Console.WriteLine("Pulse sent");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Pulse send error: {ex.Message}");
                }
            }

            void SendRobotUpdate(TcpClient client, float x, float y)
            {
                try
                {
                    if (client.Connected)
                    {
                        NetworkStream stream = client.GetStream();
                        if (stream.CanWrite)
                        {
                            // Build and send the robot update request
                            var newRobotRequestBuilder = new FlatBufferBuilder(256);

                            // Add required string fields
                            var newNameOffset = robotRequestBuilder.CreateString("testRobot");

                            // Build a bounding box (example values, adjust as needed)
                            var newCentreT = new Vec3T { X = 0, Y = 0, Z = 0 };
                            var newDimensionsT = new Vec3T { X = x, Y = y, Z = 0 };
                            var newRotationT = new Vec4T { W = 0, X = 0, Y = 0, Z = 1 };

                            var newBoundingBoxOffset = BoundingBox.CreateBoundingBox(
                                newRobotRequestBuilder,
                                newCentreT,
                                newDimensionsT,
                                newRotationT,
                                false
                            );

                            // Build the robot
                            Robot.StartRobot(newRobotRequestBuilder);
                            Robot.AddId(newRobotRequestBuilder, 123UL);
                            Robot.AddName(newRobotRequestBuilder, nameOffset);
                            Robot.AddSubscription(newRobotRequestBuilder, (ushort)20);
                            Robot.AddRate(newRobotRequestBuilder, SubscriptionRate.Full);
                            Robot.AddBoundingBox(newRobotRequestBuilder, boundingBoxOffset);

                            byte[] newRobotUpdateBytes = newRobotRequestBuilder.SizedByteArray();

                            // Create outer flatbuffer for the update
                            var builder = new FlatBufferBuilder(1024);
                            var dataVector = Payload.CreateDataVector(builder, newRobotUpdateBytes);
                            Payload.StartPayload(builder);
                            Payload.AddData(builder, dataVector);
                            var payloadOffset = Payload.EndPayload(builder);
                            var payloadsVector = State.CreatePayloadVector(builder, new[] { payloadOffset });
                            State.StartState(builder);
                            State.AddPayload(builder, payloadsVector);
                            var stateOffset = State.EndState(builder);
                            builder.Finish(stateOffset.Value);
                            byte[] stateBytes = builder.SizedByteArray();

                            // Send length prefix
                            byte[] stateLenPrefix = BitConverter.GetBytes(stateBytes.Length);
                            stream.Write(stateLenPrefix, 0, stateLenPrefix.Length);

                            // Send the actual state message
                            stream.Write(stateBytes, 0, stateBytes.Length);
                            Console.WriteLine("");
                            Console.WriteLine("Robot update sent");
                            Console.WriteLine($"Dimensions: X={newDimensionsT.X:F3}, Y={newDimensionsT.Y:F3}, Z={newDimensionsT.Z:F3}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Robot update send error: {ex.Message}");
                }

            }
        }

    }

}
catch (SocketException ex)
{
    Console.WriteLine("");
    Console.WriteLine("Connection Error");
    Console.WriteLine($"Connection failed: {ex.Message}");
    Console.WriteLine($"Error code: {ex.SocketErrorCode}");
    Console.WriteLine("Check if:");
    Console.WriteLine("1. The server IP address is correct");
    Console.WriteLine("2. The server is running");
    Console.WriteLine("3. The port number is correct");
    Console.WriteLine("4. There are no firewall blocks");
}
catch (Exception ex)
{
    Console.WriteLine("");
    Console.WriteLine("Error");
    Console.WriteLine($"Unexpected error: {ex.Message}");
}