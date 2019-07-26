using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RobotAI : MonoBehaviour
{
    GameObject[] dropAreas;
    GameObject[] stackAreas;
    NavPoint referencePoint;
    private bool targetChanged;
    public float robotDeadband;
    public NavPoint targetPos;
    private bool justReleased;
    private bool justGrabbed;
    public bool everGrabbed;
    public bool run;
    private int stackingStage;

    List<NavPoint> route = new List<NavPoint>();
    List<NavPoint> searchStack = new List<NavPoint>();
    int layerMask;

    void Start()
    {

        // Bool set up
        targetChanged = true;
        justReleased = false;
        justGrabbed = false;

        // Non-bool set up
        layerMask = 1 << 8;
        layerMask = ~layerMask;
        dropAreas = GameObject.FindGameObjectsWithTag("Drop Area");
        stackAreas = GameObject.FindGameObjectsWithTag("Stack Area");
        robotDeadband = this.gameObject.GetComponentInChildren<Collider>().bounds.size.x / 2;
        stackingStage = 0;
        referencePoint = GameObject.FindGameObjectWithTag("Ref Point").GetComponent<NavPoint>();

        // Initial route set up
        ResetNavPoints();
        GameObject[] collectableObjects = GameObject.FindGameObjectsWithTag("CollectableObject");
        targetPos = FindNearest(collectableObjects).GetComponent<NavPoint>();

    }

    List<NavPoint> CalculateRouteMain(NavPoint target)
    {

        // Resets Search Stack from previous Nav
        bool found = false;
        searchStack.Clear();
        searchStack.TrimExcess();

        // Adds This object as the first NavPoint in the search stack.
        searchStack.Add(this.gameObject.GetComponent<NavPoint>());

        // Loops until a route is found or search stack is empty
        while (!found && searchStack.Count > 0) {

            // Checks if it can reach the target and adds follow up points
            found = CalculateRouteRecursion(target, searchStack[0]);

            // Sorts list by fCost (Cost to point on current route + As the crow flies to Target)
            searchStack.Sort(delegate (NavPoint a, NavPoint b) 
            {
                return (a.fCost).CompareTo(b.fCost);
            });
        }

        // This loop runs if there is absolutly no route to target. In this case it rams headlong into a wall.
        // Under all normal circumstances this should never run. Unless a ball is spawned ontop of a wall or enclosed by obstacles there will be a way
        // If there's a will there's a way
        if(!found)
        {
            List<NavPoint> crashRoute = new List<NavPoint>();
            crashRoute.Add(target);
            return crashRoute;

        }

        // Goes back along the route from the target to This Object using NavPoint.from
        NavPoint backTrack = target;
        List<NavPoint> route = new List<NavPoint>();
        do
        {
            route.Add(backTrack);
            backTrack = backTrack.from;
        } while (backTrack != this.gameObject.GetComponent<NavPoint>());
        return route;
    }


    bool CalculateRouteRecursion(NavPoint target, NavPoint root) {

        // Set up for the initaial raycast
        Vector3 between = target.point-root.point;
        float relativeDistance = between.magnitude;

        // Set up for the offset on the raycasts
        Vector3 perp = Vector3.Cross(Vector3.up, between).normalized;

        // If there is an obstacle between the current NavPoint and the Target (ball or drop area)
        // This ignores the player and collectables using the layerMask
        bool testOne = Physics.Raycast(root.point + perp * 0.5f, between, out RaycastHit hitOne, relativeDistance, layerMask);
        bool testTwo = Physics.Raycast(root.point + perp * -0.5f, between, out RaycastHit hitTwo, relativeDistance, layerMask);
        if (testOne || testTwo)
        {

            // List of objects with their vertices already being processed
            List<GameObject> hitObjects = new List<GameObject>();

            // Creates obstVerts with the verts around the obstacle(s) hit and adds the object(s) to hitObjects
            NavPoint[] obstVerts;
            if (testOne)
            {
                obstVerts = hitOne.transform.gameObject.GetComponentsInChildren<NavPoint>();
                hitObjects.Add(hitOne.transform.gameObject);
                if (testTwo && hitTwo.transform != hitOne.transform)
                {
                    obstVerts = obstVerts.Concat(hitTwo.transform.gameObject.GetComponentsInChildren<NavPoint>()).ToArray();
                    hitObjects.Add(hitTwo.transform.gameObject);
                }
            }
            else
            {
                obstVerts = hitTwo.transform.gameObject.GetComponentsInChildren<NavPoint>();
                hitObjects.Add(hitTwo.transform.gameObject);
            }

            // Loops through all the NavPoints from around the Obstacle (Currently the only thing that can be in the way)
            NavPoint current;
            for (int i = 0; i < obstVerts.Count(); i++)
            {
                current = obstVerts[i];

                // Set up for the secondary raycasts and prepares relativeDistance
                between = current.point - root.point;
                relativeDistance = (between).magnitude;

                // If G cost (distance to the NavPoint from the origin) is higher than existing, a more optimised route already exists to here
                if (relativeDistance + root.gCost < current.gCost)
                {

                    // Set up for the offset on the raycasts
                    perp = Vector3.Cross(Vector3.up, between).normalized;

                    // Checks Line of Sight to the NavPoint from the root 
                    testOne = Physics.Raycast(root.point + perp * 0.5f, between, out hitOne, relativeDistance, layerMask);
                    testTwo = Physics.Raycast(root.point + perp * -0.5f, between, out hitTwo, relativeDistance, layerMask);
                    // /*For debug draw rays */Debug.DrawRay(root.point, between, Color.white, 10.0f);
                    if (!(testOne || testTwo))
                    {

                        // Updates g, f, and from on the NavPoint with the better route
                        current.gCost = relativeDistance + root.gCost;
                        current.fCost = current.gCost + (current.point - target.point).magnitude;
                        current.from = root;

                        // Adds the NavPoint to the search stack if it has not been already
                        if (!searchStack.Contains(current))
                        {
                            searchStack.Add(current);
                        }
                    }
                    
                    // Else adds the NavPoints around this object to the array if that obstacle is not already being checked
                    else
                    {
                        if (testOne && !hitObjects.Contains(hitOne.transform.gameObject))
                        {
                            obstVerts = obstVerts.Concat(hitOne.transform.gameObject.GetComponentsInChildren<NavPoint>()).ToArray();
                            hitObjects.Add(hitOne.transform.gameObject);
                        }
                        if (testTwo && !hitObjects.Contains(hitTwo.transform.gameObject))
                        {
                            obstVerts = obstVerts.Concat(hitTwo.transform.gameObject.GetComponentsInChildren<NavPoint>()).ToArray();
                            hitObjects.Add(hitTwo.transform.gameObject);
                        }

                    }
                    
                }
            }

        }

        // Else there is Line of Sight from the current NavPoint to the Target.
        else 
        {

            // Makes the Target's from be the current NavPoint and set found to true to break the while loop
            target.from = root;
            return true;
        }

        // Removes the curren NavPoint from the search stack
        searchStack.Remove(root);
        return false;
    }



    GameObject FindNearest(GameObject[] gameObjects)
    {
        GameObject Closest = gameObjects[0];
        float shortestDistance = (gameObjects[0].transform.position - this.transform.position).magnitude;
        foreach (GameObject obj in gameObjects)
        {
            Vector3 CurrPos = obj.transform.position;
            float relativeDistance = (CurrPos - this.transform.position).magnitude;

            if (relativeDistance < shortestDistance)
            {
                shortestDistance = relativeDistance;
                Closest = obj;
            }
        }
        return Closest;

    }

    void FixedUpdate()
    {
        if (run)
        {
            List<GameObject> collectibles = GameObject.FindGameObjectsWithTag("CollectableObject").ToList();
            /*
             * Added this to prevent the robot from going after blocks already stacked.
             */
            GameObject collectible;
            for (int i = 0; i < collectibles.Count; i++)
            {
                collectible = collectibles[i];
                // Check if the collectible is a block.
                BlockScript blockScript = collectible.GetComponent<BlockScript>();
                if (blockScript != null)
                {
                    // Check if the collectible is stacked already.
                    if (blockScript.CheckState())
                    {
                        collectibles.Remove(collectible);
                        i--;
                    }
                }
            }
            

            if (collectibles.Count > 0)
            {
                if(stackingStage> 0)
                {
                    StackingAI();
                }
                if (!GetComponent<GrabRelease>().isHoldingCollectableObject)
                {
                    if (FollowRoute() && !justReleased)
                    {
                        Grab();
                    }

                    GameObject closestCollectible = FindNearest(collectibles.ToArray());
                    NavPoint closestNavPoint = closestCollectible.GetComponent<NavPoint>();
                    if (closestNavPoint != targetPos)
                    {
                        targetPos = closestNavPoint;
                        targetChanged = true;
                        justReleased = false;
                    }
                }
                else
                {
                    if (GetComponent<GrabRelease>().grabbedObj.GetComponent<BlockScript>() != null)
                    {
                        StackingAI();
                    }
                    else if (FollowRoute() && !justGrabbed)
                    {
                        Release();
                        //Toss();
                    }
                    else
                    {
                        if (FindNearest(dropAreas).GetComponent<NavPoint>() != targetPos)
                        {
                            targetPos = FindNearest(dropAreas).GetComponent<NavPoint>();
                            targetChanged = true;
                            justGrabbed = false;
                        }
                    }
                }
            }
        }
    }
    bool FollowRoute()
    {
        if (targetChanged)
        {
            route.Clear();
            route.TrimExcess();
            route = CalculateRouteMain(targetPos);
            gameObject.GetComponent<RobotMovement>().isMoving = true;
            targetChanged = false;
            ResetNavPoints();
        }
        if (!gameObject.GetComponent<RobotMovement>().isMoving)
        {
            if (route.Count == 1)
            {
                return true;
            }
            route.RemoveAt(route.Count - 1);
        }
        Move(route[route.Count - 1].point, route[route.Count - 1].deadBand + robotDeadband + 0.05f);
        return false;
    }

    void StackingAI()
    {
        switch(stackingStage)
        {
            case 0:
                if (FindNearest(stackAreas).GetComponent<NavPoint>() != targetPos)
                {
                    targetPos = FindNearest(stackAreas).GetComponent<NavPoint>();
                    targetChanged = true;
                    justGrabbed = false;
                }
                FollowRoute();
                stackingStage++;
                goto case 1;
            case 1:
                if (route.Count() == 2 && !gameObject.GetComponent<RobotMovement>().isMoving || route.Count() < 2)
                {
                    stackingStage++;
                    goto case 2;
                } else
                {
                    layerMask = 11 << 8;
                    layerMask = ~layerMask;
                    FollowRoute();
                    layerMask = 1 << 8;
                    layerMask = ~layerMask;
                }
                if (FindNearest(stackAreas).GetComponent<NavPoint>() != targetPos)
                {
                    targetPos = FindNearest(stackAreas).GetComponent<NavPoint>();
                    targetChanged = true;
                    justGrabbed = false;
                }
                break;
            case 2:
                List<Vector3> refPoints = FindNearest(stackAreas).GetComponent<StackAreaScript>().refPoints;
                Vector3 closestRef = refPoints[0];
                float shortestDistance = (refPoints[0] - this.transform.position).magnitude;
                foreach (Vector3 refp in refPoints)
                {
                    float relativeDistance = (refp - this.transform.position).magnitude;

                    if (relativeDistance < shortestDistance)
                    {
                        shortestDistance = relativeDistance;
                        closestRef = refp;
                    }
                }
                if (referencePoint != targetPos || closestRef != referencePoint.point)
                {
                    referencePoint.gameObject.transform.position = closestRef;
                    referencePoint.point = closestRef;
                    targetPos = referencePoint;
                    targetChanged = true;
                }
                if (FollowRoute())
                {
                    stackingStage++;
                    goto case 3;
                }
                break;
                // Inbetween these cases add veticality
            case 3:
                Move(FindNearest(stackAreas).GetComponent<StackAreaScript>().nextPos,robotDeadband+.05f+gameObject.GetComponent<GrabRelease>().grabbedObj.GetComponent<NavPoint>().deadBand);
                if(!gameObject.GetComponent<RobotMovement>().isMoving)
                {
                    stackingStage++;
                    Release();
                    goto case 4;
                }
                break;
            case 4:
                Move(referencePoint.point, .05f, true);
                if (!gameObject.GetComponent<RobotMovement>().isMoving)
                {
                    stackingStage = 0;
                }
                break;
        }
    }

    /**
     * Doesn't include the NavPoint on the Robot game object.
     */
    private void ResetNavPoints()
    {
        // Obstacles
        GameObject[] Obstacles = GameObject.FindGameObjectsWithTag("Obstacle");
        List<GameObject> ObstacleVertices = new List<GameObject>();
        foreach(GameObject obs in Obstacles) {
            for (int i = 0; i < obs.transform.childCount; i++) {
                ObstacleVertices.Add(obs.transform.GetChild(i).gameObject);
            }
        }
        foreach (GameObject vertex in ObstacleVertices) {
            vertex.GetComponent<NavPoint>().ResetValues();
        }

        // Collectibles
        GameObject[] Collectibles = GameObject.FindGameObjectsWithTag("CollectableObject");
        foreach (GameObject collectible in Collectibles)
        {
            collectible.GetComponent<NavPoint>().ResetValues();
        }

        // Drop Areas
        foreach (GameObject area in dropAreas)
        {
            area.GetComponent<NavPoint>().ResetValues();
        }

        // Stack Areas
        foreach (GameObject area in stackAreas)
        {
            area.GetComponent<NavPoint>().ResetValues();
        }
    }

    void Move(Vector3 position, float deadBand, bool moveBack = false)
    {
        gameObject.GetComponent<RobotMovement>().Move(position,deadBand, moveBack);
    }

    void Grab() {
        everGrabbed = true;
        if (gameObject.GetComponent<GrabRelease>().isHoldingCollectableObject) {
            return;
        }
        if (gameObject.GetComponent<GrabRelease>().Grab())
        {
            justGrabbed = true;
        }
    }

    void Release() {
        justReleased = true;
        if (!gameObject.GetComponent<GrabRelease>().isHoldingCollectableObject)
        {
            return;
        }
        gameObject.GetComponent<GrabRelease>().Release();
    }
    //to toss the ball
    void Toss()
    {
        justReleased = true;
        if (!gameObject.GetComponent<GrabRelease>().isHoldingCollectableObject)
        {
            return;
        }
        gameObject.GetComponent<GrabRelease>().Toss();
    }
}
