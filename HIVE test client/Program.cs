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
            /*
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
            ); */

            // Build the robot
            Robot.StartRobot(robotRequestBuilder);
            Robot.AddId(robotRequestBuilder, 123UL);
            Robot.AddName(robotRequestBuilder, nameOffset);
            Robot.AddSubscription(robotRequestBuilder, (ushort)20);
            Robot.AddRate(robotRequestBuilder, SubscriptionRate.Full);
            //Robot.AddBoundingBox(robotRequestBuilder, boundingBoxOffset); // Uncomment if bounding box is used
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
                byte[] responseData = ReadServerResponse(stream);   // Handles reading data

                if (responseData.Length > 0)
                {
                    ProcessServerResponse(responseData, responseData.Length);   // Handles parsing the state structure and processing
                }

                CreateNodesInSquarePattern(stream, 123UL);  // Create nodes in a square pattern

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

            // DEBUG: Print the raw bytes in a readable format
            void ShowRawData(byte[] responseData, int totalBytesRead)
            {
                Console.WriteLine("");
                Console.WriteLine("Raw Response Data");
                Console.WriteLine($"Hex: {BitConverter.ToString(responseData, 0, Math.Min(totalBytesRead, 100))}..."); // First 100 bytes
                Console.WriteLine($"As ASCII: {System.Text.Encoding.ASCII.GetString(responseData, 0, Math.Min(totalBytesRead, 100))}...");
            }

            // Calculate how many bytes to read
            int RawBytes(NetworkStream stream, int responseLength, byte[] responseData)
            {
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

                return totalBytesRead;
            }

            // Read the length prefix
            int ReadLengthPrefix(NetworkStream stream)
            {
                // Read the first 4 bytes to get the length prefix
                byte[] lengthBuffer = new byte[4];
                int lengthBytesRead = 0;
                while (lengthBytesRead < 4)
                {
                    // Read remaining bytes after length prefix
                    lengthBytesRead += stream.Read(lengthBuffer, lengthBytesRead, 4 - lengthBytesRead);
                }

                int responseLength = BitConverter.ToInt32(lengthBuffer, 0);
                Console.WriteLine("");
                Console.WriteLine($"Server response length: {responseLength} bytes");

                return responseLength;
            }

            // Read the server response
            byte[] ReadServerResponse(NetworkStream stream)
            {
                if (!stream.DataAvailable)
                {
                    Console.WriteLine("No data available from server");
                    return Array.Empty<byte>();
                }

                int responseLength = ReadLengthPrefix(stream);
                byte[] responseData = new byte[responseLength];
                int totalBytesRead = RawBytes(stream, responseLength, responseData);

                ShowRawData(responseData, totalBytesRead);  // DEBUG use only

                return responseData;
            }

            // Process the server response
            void ProcessServerResponse(byte[] responseData, int totalBytesRead)
            {
                try
                {
                    // Try to parse as State first
                    var byteBuffer = new ByteBuffer(responseData);
                    var state = State.GetRootAsState(byteBuffer);

                    if (state.PayloadLength == 0)
                    {
                        Console.WriteLine("");
                        Console.WriteLine("No payloads have been parsed");
                        return;
                    }

                    Console.WriteLine("");
                    Console.WriteLine($"Successfully parsed as State with {state.PayloadLength} payload(s)");

                    // Go through each payload
                    for (int i = 0; i < state.PayloadLength; i++)
                    {
                        // Get the payload
                        var payload = state.Payload(i);
                        if (payload.HasValue)
                        {
                            // Get the data bytes from the payload
                            var dataSegment = payload.Value.GetDataBytes();
                            if (dataSegment.HasValue)
                            {
                                // Extract the byte array
                                var segment = dataSegment.Value;
                                byte[] flatBufferBytes = segment.Array != null
                                    ? segment.Array.Skip(segment.Offset).Take(segment.Count).ToArray()
                                    : Array.Empty<byte>();

                                Console.WriteLine("");
                                Console.WriteLine($"Payload {i + 1}");
                                Console.WriteLine($"Payload size: {flatBufferBytes.Length} bytes");
                                Console.WriteLine($"Hex: {BitConverter.ToString(flatBufferBytes)}");
                                Console.WriteLine($"As string: {System.Text.Encoding.UTF8.GetString(flatBufferBytes)}");

                                // Process the payload data
                                ProcessPayloadData(flatBufferBytes, i);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("");
                    Console.WriteLine($"Failed to parse as State: {ex.Message}");

                    // Final fallback - just show what we got
                    Console.WriteLine("");
                    Console.WriteLine("FINAL RAW DATA");
                    string fullText = System.Text.Encoding.UTF8.GetString(responseData, 0, totalBytesRead);
                    Console.WriteLine($"Full response as text: {fullText}");
                }
            }

            // Process individual payload data
            void ProcessPayloadData(byte[] flatBufferBytes, int payloadIndex)
            {
                try
                {
                    // Try to parse the payload as Entity
                    var entityBuffer = new ByteBuffer(flatBufferBytes);
                    var entity = Entity.GetRootAsEntity(entityBuffer);

                    Console.WriteLine($"Entity type: {entity.EntityType}");

                    if (entity.EntityType == EntityUnion.Robot)
                    {
                        // Process robot data
                        ProcessRobotData(entity);
                    }
                    else if (entity.EntityType == EntityUnion.Node)
                    {
                        // Process node data
                        ProcessNodeData(entity);
                    }
                    else
                    {
                        Console.WriteLine("");
                        Console.WriteLine($"Entity is: {entity.EntityType}");
                    }
                }
                catch (Exception entityEx)
                {
                    Console.WriteLine("");
                    Console.WriteLine($"Could not parse payload as Entity: {entityEx.Message}");
                }
            }

            // Process robot data
            void ProcessRobotData(Entity entity)
            {
                // Output robot data
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
                }
                else
                {
                    Console.WriteLine("Bounding Box: null or not present");
                }
            }

            // Process node data
            void ProcessNodeData(Entity entity)
            {
                // Output node data
                var node = entity.Entity_AsNode();
                Console.WriteLine("");
                Console.WriteLine("Node Data Found");
                Console.WriteLine($"Node ID: {node.Id}");
                
                // Extract position details
                var position = node.Position;
                if (position.HasValue)
                {
                    var pos = position.Value;
                    Console.WriteLine($"Position: X={pos.X:F3}, Y={pos.Y:F3}, Z={pos.Z:F3}");
                }

                // Extract rotation details
                var rotation = node.Rotation;
                if (rotation.HasValue)
                {
                    var rot = rotation.Value;
                    Console.WriteLine($"Rotation: W={rot.W:F3}, X={rot.X:F3}, Y={rot.Y:F3}, Z={rot.Z:F3}");
                }

                // Extract velocity details
                var velocity = node.Velocity;
                if (velocity.HasValue)
                {
                    var vel = velocity.Value;
                    Console.WriteLine($"Velocity: X={vel.X:F3}, Y={vel.Y:F3}, Z={vel.Z:F3}");
                }

                Console.WriteLine($"Error: {node.Error}");
            }

            // Create nodes in a square pattern with position changes
            void CreateNodesInSquarePattern(NetworkStream stream, ulong robotID)
            {
                Console.WriteLine("");
                Console.WriteLine("Starting to create nodes in square pattern...");

                float x = 0;
                float y = 0;
                float z = 0;

                // Move right
                Console.WriteLine("");
                Console.WriteLine("Moving right...");
                for (int a = 0; a < 10; a++)
                {
                    x = x + 0.1f;
                    CreateNodeAndReadResponse(stream, robotID, x, y, z);
                    Thread.Sleep(100); // Small delay to avoid overwhelming the server
                }

                // Move up
                Console.WriteLine("");
                Console.WriteLine("Moving up...");
                for (int b = 0; b < 10; b++)
                {
                    y = y + 0.1f;
                    CreateNodeAndReadResponse(stream, robotID, x, y, z);
                    Thread.Sleep(100);
                }

                // Move left
                Console.WriteLine("");
                Console.WriteLine("Moving left...");
                for (int a = 0; a < 10; a++)
                {
                    x = x - 0.1f;
                    CreateNodeAndReadResponse(stream, robotID, x, y, z);
                    Thread.Sleep(100);
                }

                // Move down
                Console.WriteLine("");
                Console.WriteLine("Moving down...");
                for (int b = 0; b < 10; b++)
                {
                    y = y - 0.1f;
                    CreateNodeAndReadResponse(stream, robotID, x, y, z);
                    Thread.Sleep(100);
                }

                Console.WriteLine("");
                Console.WriteLine("Square pattern completed!");
            }

            // Create a node and read the server response
            void CreateNodeAndReadResponse(NetworkStream stream, ulong robotID, float xPosition, float yPosition, float zPosition)
            {
                try
                {
                    Console.WriteLine("");
                    Console.WriteLine($"Creating node at position: X={xPosition:F3}, Y={yPosition:F3}, Z={zPosition:F3}");

                    // Build and send the node request
                    var nodeRequestBuilder = new FlatBufferBuilder(256);

                    // Build the node
                    Node.StartNode(nodeRequestBuilder);
                    Node.AddId(nodeRequestBuilder, robotID);
                    Node.AddError(nodeRequestBuilder, 0.0f);
                    Node.AddVelocity(nodeRequestBuilder, Vec3.CreateVec3(nodeRequestBuilder, 0.0f, 0.0f, 0.0f));
                    Node.AddRotation(nodeRequestBuilder, Vec4.CreateVec4(nodeRequestBuilder, 1.0f, 0.0f, 0.0f, 0.0f));
                    Node.AddPosition(nodeRequestBuilder, Vec3.CreateVec3(nodeRequestBuilder, xPosition, yPosition, zPosition));
                    var nodeOffset = Node.EndNode(nodeRequestBuilder);

                    nodeRequestBuilder.Finish(nodeOffset.Value);
                    byte[] nodeRequestBytes = nodeRequestBuilder.SizedByteArray();

                    // Create outer flatbuffer
                    var builder = new FlatBufferBuilder(1024);
                    var dataVector = Payload.CreateDataVector(builder, nodeRequestBytes);
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

                    Console.WriteLine($"Sent {stateBytes.Length} bytes to server");

                    // Wait for server to process and read response
                    Thread.Sleep(1000); // Wait for server processing

                    if (stream.DataAvailable)
                    {
                        byte[] responseData = ReadServerResponse(stream);
                        if (responseData.Length > 0)
                        {
                            ProcessServerResponse(responseData, responseData.Length);
                        }
                    }
                    else
                    {
                        Console.WriteLine("No immediate response from server for node creation");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("");
                    Console.WriteLine($"Error creating node: {ex.Message}");
                }
            }

            // Send a pulse to keep the connection alive
            void SendPulse(TcpClient client)
            {
                try
                {
                    if (client.Connected)
                    {
                        NetworkStream stream = client.GetStream();
                        if (stream.CanWrite)
                        {
                            // Send a simple pulse message (for example, just a magic number)
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