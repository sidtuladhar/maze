using UnityEngine;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using Unity.AI.Navigation;
using System.Linq;

public class MazeGenerator : MonoBehaviour
{
    [SerializeField] private List<GameObject> chunkPrefabs;  // Assign your chunk assets in inspector
    [SerializeField] private List<GameObject> singleChunkPrefabs;  // Chunks that will only be used once
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private Material exitMaterial; // Assign the glowing material in inspector


    [SerializeField] private int maxDepth = 10;  // Maximum recursion depth
    private int currentDepth = 0;
    private List<ConnectionPoint> openConnections = new List<ConnectionPoint>();
    public List<GameObject> placedChunks = new List<GameObject>();

    [SerializeField] private GameObject batteryPrefab;
    [SerializeField] private int batteriesPerMaze = 3;

    void Start()
    {
        GenerateMaze(maxDepth);
    }

    private void GenerateMaze(int maxDepth)
    {
        currentDepth = 0;

        // Place first chunk at origin
        GameObject firstChunk = chunkPrefabs[Random.Range(0, chunkPrefabs.Count)];
        GameObject firstInstance = Instantiate(firstChunk, Vector3.zero, Quaternion.identity);
        firstInstance.transform.parent = transform;

        // Add first chunk to placedChunks dictionary
        placedChunks.Add(firstInstance);
        
        // Add single use chunks to chunkPrefabs (they will be removed after being placed)
        chunkPrefabs.AddRange(singleChunkPrefabs);

        ConnectionPoint[] connections = firstInstance.GetComponentsInChildren<ConnectionPoint>();
        openConnections.AddRange(connections);

        // Start recursive generation with depth limit
        while (openConnections.Count > 0 && currentDepth < maxDepth)
        {
            int connectionIndex = Random.Range(0, openConnections.Count);
            ConnectionPoint currentConnection = openConnections[connectionIndex];

            if (TestConnection(currentConnection))
            {
                currentDepth++;  // Increment depth when adding a new chunk
                openConnections.RemoveAt(connectionIndex);
            }
            else
            {
                openConnections.RemoveAt(connectionIndex);
                Debug.Log($"Dead end at {currentConnection.transform.position}");
            }
        }

        if (currentDepth >= maxDepth)
        {
            Debug.Log($"Reached max depth: {maxDepth}.");
        }

        GetComponent<NavMeshSurface>().BuildNavMesh();

        // Pick exit
        if (placedChunks.Count > 0)
        {
            // Pick a random chunk from all placed chunks.
            GameObject randomChunk = placedChunks[Random.Range(0, placedChunks.Count)];

            // Get all connection points in that chunk.
            ConnectionPoint[] randomConnections = randomChunk.GetComponentsInChildren<ConnectionPoint>();

            int maxAttempts = 100;  // Prevent infinite loop
            int attempts = 0;

            if (randomConnections.Length > 0)
            {
                // Pick a random connection point.
                ConnectionPoint exitConnection = randomConnections[Random.Range(0, randomConnections.Length)];
                ConnectionPoint enemySpawnConnection = randomConnections[Random.Range(0, randomConnections.Length)];

                // Find a connection point for the exit
                while (exitConnection.DeadEndPrefab.activeSelf == false && attempts < maxAttempts)
                {
                    exitConnection = randomConnections[Random.Range(0, randomConnections.Length)];
                    randomChunk = placedChunks[Random.Range(0, placedChunks.Count)];
                    randomConnections = randomChunk.GetComponentsInChildren<ConnectionPoint>();
                    attempts++;
                }

                // Find a connection point for the enemy spawn
                while (enemySpawnConnection.DeadEndPrefab.activeSelf == true && attempts < maxAttempts)
                {
                    enemySpawnConnection = randomConnections[Random.Range(0, randomConnections.Length)];
                    randomChunk = placedChunks[Random.Range(0, placedChunks.Count)];
                    randomConnections = randomChunk.GetComponentsInChildren<ConnectionPoint>();
                    attempts++;
                }
                SpawnEnemy(enemySpawnConnection);

                if (exitConnection.DeadEndPrefab.TryGetComponent<MeshRenderer>(out var renderer))
                {
                    renderer.material = exitMaterial;
                    GameObject lightGO = new GameObject("ExitLight");
                    Light pointLight = lightGO.AddComponent<Light>();
                    pointLight.type = LightType.Point;
                    pointLight.range = 20f;
                    pointLight.intensity = 20f;
                    pointLight.color = exitMaterial.GetColor("_EmissionColor");

                    // Parent and position the light
                    lightGO.transform.parent = exitConnection.DeadEndPrefab.transform;
                    lightGO.transform.localPosition = Vector3.forward * 0.5f;

                    if (!exitConnection.DeadEndPrefab.TryGetComponent<BoxCollider>(out var triggerCollider))
                    {
                        triggerCollider = exitConnection.DeadEndPrefab.AddComponent<BoxCollider>();
                    }
                    triggerCollider.isTrigger = true;
                    exitConnection.DeadEndPrefab.AddComponent<ExitTrigger>();
                }

            }
        }

        SpawnBatteries();
        SpawnPlayer();
    }

    private bool TestConnection(ConnectionPoint connectionPoint)
    {
        GameObject randomChunk = chunkPrefabs[Random.Range(0, chunkPrefabs.Count)];

        Vector3 targetPosition = transform.TransformPoint(connectionPoint.transform.position) + connectionPoint.connectionOffset;

        ConnectionPoint[] testChunkConnections = randomChunk.GetComponentsInChildren<ConnectionPoint>();
        ConnectionPoint testConnection = testChunkConnections[Random.Range(0, testChunkConnections.Length)];

        // Get the world position of the potential connection point
        Vector3 matchPosition = testConnection.connectionOffset;

        Vector3 alignmentOffset = targetPosition - matchPosition;

        int[] rotations = { 0, 1, 2, 3 };
        rotations = rotations.OrderBy(x => Random.value).ToArray();

        foreach (int rotation in rotations)
        {

            GameObject testChunk2 = Instantiate(randomChunk);

            testChunk2.transform.position = alignmentOffset;

            ConnectionPoint[] tempConnectionPoints = testChunk2.GetComponentsInChildren<ConnectionPoint>();

            foreach (ConnectionPoint conn in tempConnectionPoints)
            {
                Quaternion rot = Quaternion.AngleAxis(90f * rotation, Vector3.up);
                conn.connectionOffset = rot * conn.connectionOffset;
            }

            testChunk2.transform.RotateAround(targetPosition, Vector3.up, 90f * rotation);
            Debug.Log(testChunk2.transform.position);

            Collider testCollider = testChunk2.GetComponent<Collider>();
            bool collisionFound = false;

            foreach (GameObject placedChunk in placedChunks)
            {
                Collider placedCollider = placedChunk.GetComponent<Collider>();
                if (placedCollider == null) continue;

                Vector3 direction;
                float distance;
                float margin = 1f;

                bool overlapped = Physics.ComputePenetration(
                    testCollider, testChunk2.transform.position, testChunk2.transform.rotation,
                    placedCollider, placedChunk.transform.position, placedChunk.transform.rotation,
                    out direction, out distance);

                if (overlapped && distance >= margin)
                {
                    collisionFound = true;
                    Destroy(testChunk2);
                }
            }
            if (!collisionFound) // If no collision was found, place the chunk
            {

                testChunk2.transform.parent = transform;
                placedChunks.Add(testChunk2);
                var testChunk2Connections = testChunk2.GetComponentsInChildren<ConnectionPoint>();
                var newConnections = testChunk2Connections.Where(p => p != testConnection); // Get all connections except the one we used
                openConnections.AddRange(newConnections); // Add new connections to the list
                if (connectionPoint.DeadEndPrefab != null)
                {
                    connectionPoint.DeadEndPrefab.SetActive(false);
                    ConnectionPoint matchingConnection = tempConnectionPoints
                        .FirstOrDefault(conn => conn.DeadEndPrefab != null &&
                                        conn.DeadEndPrefab.name == testConnection.DeadEndPrefab.name);
                    if (matchingConnection != null)
                    {
                        matchingConnection.DeadEndPrefab.SetActive(false);
                    }
                }

                if (singleChunkPrefabs.Contains(randomChunk))
                {
                    Debug.Log("Removing single chunk" + chunkPrefabs.Count);
                    chunkPrefabs.Remove(randomChunk);
                }
                return true;
            }
        }
        return false;
    }

    public void RegenerateMaze()
    {
        GameObject existingPlayer = GameObject.FindWithTag("Player");


        // Clean up existing maze
        foreach (GameObject chunk in placedChunks)
        {
            Destroy(chunk);
        }
        placedChunks.Clear();
        openConnections.Clear();
        currentDepth = 0;
        maxDepth += 5;

        // Generate new maze
        GenerateMaze(maxDepth);

        if (existingPlayer != null)
        {
            CharacterController controller = existingPlayer.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            existingPlayer.transform.position = new Vector3(0f, 1f, 0f);

            if (controller != null)
            {
                controller.enabled = true;
            }

        }
        SpawnBatteries();
    }

    private void SpawnBatteries()
    {
        for (int i = 0; i < batteriesPerMaze; i++)
        {
            GameObject randomChunk = placedChunks[Random.Range(0, placedChunks.Count)];
            Vector3 randomPosition = randomChunk.transform.position + new Vector3(
                Random.Range(-4f, 4f),
                1f,
                Random.Range(-4f, 4f)
            );

            Instantiate(batteryPrefab, randomPosition, Quaternion.identity);
        }
    }

    private void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player prefab not assigned!");
            return;
        }

        Vector3 spawnPosition = placedChunks[0].transform.position + new Vector3(0, 1f, 0);
        GameObject player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
    }

    public void SpawnEnemy(ConnectionPoint spawnPoint)
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("Enemy prefab not assigned!");
            return;
        }

        // Get the position of the connection point
        Vector3 spawnPosition = spawnPoint.transform.position;

        // Instantiate the enemy at the connection point position
        GameObject enemy = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
        enemy.transform.parent = transform;
    }
}