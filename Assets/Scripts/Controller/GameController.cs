﻿/**
Connecting Squares, a Unity Open Source Puzzle game
Copyright (C) 2017  Alain Shakour

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
**/
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System;

/// <summary>
/// Controls most of the game logic.
/// 
/// "Block" is a square of color.
/// "Cell" is the same as a "block", but only in context of grid computations.
/// "Arena" are all the blocks that are displayed except the floor and the player.
/// "Player" is the two blocks the actual human player uses.
/// "Floor" is the bottom blocks that cannot disappear.
/// "Ceiling" is the small line of grey blocks at the top.
/// "Columns" are the two columns of block at each size of the arena.
/// "Next Player" is the block displaying the next colors the player will use.
/// "Flying Cell" is a block that is in the air after blocks have disappeared below it.
/// "Connecting Squares" is a chain of block from the left column to the right column.
/// "Cycle". A cycle starts when the "player" is created and ends when it is at its final position, on top of another block.
/// </summary>
public class GameController : MonoBehaviour
{

    public GameObject playerTemplate;
    public GameObject nextPlayerTemplate;
    public GameObject columnCellTemplate;
    public GameObject floorTemplate;

    public GameObject leftColumn;
    public GameObject rightColumn;
    public GameObject arenaLines;
    public GameObject mainCamera;
    public GameObject debugGlobalController;
    public Text scoreText;
    public int currentScore = 0;

    private const int leftColumnIndex = 0;
    private const int rightColumnIndex = 6;
    private const int columnSize = 15;
    private const float blocksAutofallSpeed = 0.2f;
    private const float columnRecreationSpeed = 0.1f;
    private IEnumerator coroutine;
    private GameObject nextPlayer;
    private GlobalController globalController;
    private string scoreFormat = "000000000000000";
    private float timeToNextSpeedUp;
    private const float timeBetweenEachSpeedUp = 45;


    void Start()
    {
        var menuGlobalController = GameObject.FindGameObjectWithTag(TagNames.GlobalController);
        if (menuGlobalController == null)
        {
            debugGlobalController.SetActive(true);
            globalController = debugGlobalController.GetComponent<GlobalController>();
            globalController.GameSpeed = 5;
        }
        else
        {
            globalController = menuGlobalController.GetComponent<GlobalController>();
        }
        CreateColumns();
        CreateNextPlayer();
        CreatePlayer();
        CreateFloor();
        CreateCeiling();
        SetupCamera();
        scoreText.text = currentScore.ToString(scoreFormat);
        timeToNextSpeedUp = timeBetweenEachSpeedUp;
    }

    void Update()
    {
        bool escape = Input.GetButtonDown("Escape");
        if (escape)
        {
            EndGame();
        }
        timeToNextSpeedUp -= Time.deltaTime;
        if (timeToNextSpeedUp < 0)
        {
            timeToNextSpeedUp = Math.Min(globalController.GameSpeed * 6, timeBetweenEachSpeedUp);
            if (globalController.GameSpeed < 20)
            {
                globalController.GameSpeed++;
            }
        }
    }

    /// <summary>
    /// Camera is setup depending on arena size.
    /// </summary>
    private void SetupCamera()
    {
        mainCamera.transform.position = new Vector3((rightColumnIndex + leftColumnIndex) / 2, columnSize * 0.6f, -10);
        mainCamera.GetComponent<Camera>().orthographicSize = (rightColumnIndex - leftColumnIndex) * columnSize * 0.12f;
    }


    /// <summary>
    /// Invoke coroutine to allow small animations of falling blocks during combos and columns creation.
    /// </summary>
    public void DoNewCycle()
    {
        coroutine = StepByStepBlockMovement(blocksAutofallSpeed);
        StartCoroutine(coroutine);

    }

    /// <summary>
    /// Invoke at the end of the cycle to compute possible chains.
    /// </summary>
    /// <param name="waitTime"></param>
    /// <returns></returns>
    private IEnumerator StepByStepBlockMovement(float waitTime)
    {
        List<Transform> flyingCell = new List<Transform>();
        int scoreIteration = 1;
        do
        {
            List<Transform> allCells = FindAllCells();
            List<int> removedCells = ComputeConnectingSquares(ref allCells);
            currentScore += removedCells.Count * removedCells.Sum() * globalController.GameSpeed * 10 * scoreIteration;
            scoreText.text = currentScore.ToString(scoreFormat);
            flyingCell = ComputeFlyingCellNewPosition(allCells);
            if (flyingCell.Count != 0)
            {
                yield return new WaitForSeconds(waitTime);
            }
            while (MoveCellToLowerPosition(flyingCell))
            {
                yield return new WaitForSeconds(waitTime);
            }
            scoreIteration *= 2;
        } while (flyingCell.Count != 0);
        yield return RecreateColumns();

        if (!IsEndGame())
        {
            CreatePlayer();
        }
        else
        {
            EndGame();
        }
    }

    #region Block color
    /// <summary>
    /// Retrieve the colors of the "Next Player" display.
    /// </summary>
    /// <returns></returns>
    public List<int> GetNextPlayerColors()
    {
        BlockColor[] blockColors = nextPlayer.GetComponentsInChildren<BlockColor>();
        List<int> colors = blockColors.Select(c => c.Color).ToList();
        return colors;
    }
    #endregion

    #region Create every objects
    /// <summary>
    /// Create columns of block at each size of the arena.
    /// </summary>
    private void CreateColumns()
    {
        bool leftColumnCreation;
        bool rightColumnCreation;
        do
        {
            leftColumnCreation = CreateColumnCell(leftColumn, leftColumnIndex, columnSize, TagNames.LeftColumn);
            rightColumnCreation = CreateColumnCell(rightColumn, rightColumnIndex, columnSize, TagNames.RightColumn);
        } while (leftColumnCreation || rightColumnCreation);
    }

    /// <summary>
    /// Recreate the columns with a small delay (for animation).
    /// </summary>
    /// <returns></returns>
    private IEnumerator RecreateColumns()
    {
        bool leftColumnCreation;
        bool rightColumnCreation;
        do
        {
            leftColumnCreation = CreateColumnCell(leftColumn, leftColumnIndex, columnSize, TagNames.LeftColumn);
            rightColumnCreation = CreateColumnCell(rightColumn, rightColumnIndex, columnSize, TagNames.RightColumn);
            yield return new WaitForSeconds(columnRecreationSpeed);
        } while (leftColumnCreation || rightColumnCreation);
    }

    /// <summary>
    /// Create one of the cell of a column.
    /// </summary>
    /// <param name="column">Parent column object</param>
    /// <param name="worldPosition">Position in world space</param>
    /// <param name="nbCellsPerColumn">Number of cells for the column</param>
    /// <param name="tagName">Tag name</param>
    /// <returns>If a cell has been created</returns>
    private bool CreateColumnCell(GameObject column, int worldPosition, int nbCellsPerColumn, string tagName)
    {
        BlockColor[] cells = column.GetComponentsInChildren<BlockColor>();
        if (cells.Count() < nbCellsPerColumn)
        {
            GameObject newCell = Instantiate(columnCellTemplate, new Vector2(worldPosition, cells.Count() + 1f), Quaternion.identity, column.transform);
            newCell.tag = tagName;
            newCell.GetComponent<BlockController>().ChangeNameForDebug();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Create the bottom line of blocks.
    /// </summary>
    private void CreateFloor()
    {
        float padding = leftColumnIndex;
        while (padding <= rightColumnIndex)
        {
            GameObject floor = Instantiate(floorTemplate, new Vector2(padding, 0), Quaternion.identity, arenaLines.transform);
            floor.GetComponent<BlockColor>().Color = 6;
            floor.name = "Floor";
            padding++;
        }

    }

    /// <summary>
    /// Create the small grey lines of background blocks.
    /// </summary>
    private void CreateCeiling()
    {
        float ceilingSize = 0.5f;
        float padding = leftColumnIndex - 0.5f * ceilingSize;
        while (padding <= rightColumnIndex + 0.5f * ceilingSize)
        {
            GameObject ceiling = Instantiate(floorTemplate, new Vector2(padding, columnSize + ceilingSize * .5f), Quaternion.identity, arenaLines.transform);
            ceiling.transform.localScale = new Vector2(ceilingSize, ceilingSize);
            ceiling.GetComponent<BlockColor>().Color = 7;
            ceiling.GetComponent<SpriteRenderer>().sortingLayerName = SortingLayerNames.Background;
            ceiling.GetComponent<BoxCollider2D>().enabled = false;
            ceiling.name = "Ceiling";
            padding += ceilingSize;
        }
    }

    /// <summary>
    /// Compute if the "ceiling" has been reached.
    /// </summary>
    /// <returns>if the player has lost</returns>
    private bool IsEndGame()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(TagNames.Player);

        // Check the maximum Y position of all the blocks in the current game
        float maxPlayerPositionInY = players.Length > 0 ? players.Max(p => p.transform.position.y) : 0;

        return maxPlayerPositionInY >= columnSize;
    }

    /// <summary>
    /// Create new blocks for the player.
    /// </summary>
    private void CreatePlayer()
    {
        Instantiate(
                playerTemplate,
                new Vector2((int)System.Math.Round((leftColumnIndex + rightColumnIndex) / 2d, System.MidpointRounding.AwayFromZero), columnSize + .5f),
                playerTemplate.transform.rotation);
    }

    /// <summary>
    /// Create the small display of the next colors on the right of the screen.
    /// </summary>
    private void CreateNextPlayer()
    {
        nextPlayer = Instantiate(
            nextPlayerTemplate,
            new Vector2(rightColumnIndex + 3, columnSize - 3),
            nextPlayerTemplate.transform.rotation);
    }

    /// <summary>
    /// Update the small display of the next colors.
    /// </summary>
    public void ChangeNextPlayerColors()
    {
        BlockColor[] blockColors = nextPlayer.GetComponentsInChildren<BlockColor>();
        foreach (BlockColor blockColor in blockColors)
        {
            blockColor.Color = globalController.GetRandomColor();
        }
    }
    #endregion

    #region Compute Connecting Squares
    /// <summary>
    /// Remove chained cells.
    /// </summary>
    /// <param name="allCells"></param>
    /// <returns></returns>
    private List<int> ComputeConnectingSquares(ref List<Transform> allCells)
    {
        Dictionary<int, List<Transform>> sortedColors = allCells.GroupBy(c => c.GetComponent<BlockColor>().Color).ToDictionary(c => c.Key, c => c.ToList());
        List<int> nbRemovedCells = new List<int>();
        foreach (var color in sortedColors.Keys) // Groups are sort by colors
        {
            List<List<Transform>> groupsOfCells = GroupConnectedCells(sortedColors[color]);
            DisplayGroupNumber(groupsOfCells);
            nbRemovedCells.Add(SearchConnectedLines(ref allCells, groupsOfCells));
        }
        return nbRemovedCells;
    }

    /// <summary>
    /// Find all cells of the arena.
    /// </summary>
    /// <returns></returns>
    private List<Transform> FindAllCells()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(TagNames.Player);

        Transform[] leftCells = leftColumn.GetComponentsInChildren<Transform>();
        Transform[] rightCells = rightColumn.GetComponentsInChildren<Transform>();
        Transform[] playersTransform = players.SelectMany(p => p.GetComponentsInChildren<Transform>()).ToArray();

        List<Transform> allCells = leftCells.Concat(playersTransform).Concat(rightCells).Where(p => p.GetComponent<BlockController>() != null).OrderBy(p => p.position.y).ThenBy(p => p.position.x).ToList();
        return allCells;
    }

    /// <summary>
    /// Group cells of same color that are near each other.
    /// </summary>
    /// <param name="sortedCells"></param>
    /// <returns></returns>
    private static List<List<Transform>> GroupConnectedCells(List<Transform> sortedCells)
    {
        Queue<Transform> stackedCells = new Queue<Transform>(sortedCells);
        List<List<Transform>> groupsOfCells = new List<List<Transform>>();
        while (stackedCells.Count != 0)
        {
            Transform cellToCompare = stackedCells.Dequeue();
            List<Transform> currentGroup = null;
            List<List<Transform>> groupsToRemove = new List<List<Transform>>();
            foreach (var cellGroup in groupsOfCells)
            {
                foreach (var cell in cellGroup)
                {
                    if (MathExtensions.SafeComparison(cellToCompare.position.x - 1, cell.position.x) && MathExtensions.SafeComparison(cellToCompare.position.y, cell.position.y)
                        || MathExtensions.SafeComparison(cellToCompare.position.x + 1, cell.position.x) && MathExtensions.SafeComparison(cellToCompare.position.y, cell.position.y)
                        || MathExtensions.SafeComparison(cellToCompare.position.y - 1, cell.position.y) && MathExtensions.SafeComparison(cellToCompare.position.x, cell.position.x)
                        || MathExtensions.SafeComparison(cellToCompare.position.y + 1, cell.position.y) && MathExtensions.SafeComparison(cellToCompare.position.x, cell.position.x))
                    {
                        if (currentGroup != null) // Merge Group
                        {
                            currentGroup.AddRange(cellGroup);
                            groupsToRemove.Add(cellGroup);
                        }
                        else { // Add to a Group
                            cellGroup.Add(cellToCompare);
                            currentGroup = cellGroup;
                        }
                        break;
                    }
                }
            }
            groupsToRemove.ForEach(x => groupsOfCells.Remove(x));
            if (currentGroup == null)
            {
                var cellGroup = new List<Transform>();
                cellGroup.Add(cellToCompare);
                groupsOfCells.Add(cellGroup);
            }
        }

        return groupsOfCells;
    }

    /// <summary>
    /// Display cell's group if DebugText is enabled on block controller (debug purpose).
    /// </summary>
    /// <param name="groupsOfCells"></param>
    private void DisplayGroupNumber(List<List<Transform>> groupsOfCells)
    {
        for (int i = 0; i < groupsOfCells.Count; i++)
        {
            foreach (var cell in groupsOfCells[i])
            {
                cell.GetComponent<BlockController>().ChangeText(i.ToString());
            }
        }
    }

    /// <summary>
    /// Remove connected lines and return the number of removed cells.
    /// </summary>
    /// <param name="allCells"></param>
    /// <param name="groupsOfCells"></param>
    /// <returns>Number of removed cells</returns>
    private int SearchConnectedLines(ref List<Transform> allCells, List<List<Transform>> groupsOfCells)
    {
        int nbRemoved = 0;
        foreach (var cellGroup in groupsOfCells)
        {
            if (cellGroup.Any(x => x.tag == TagNames.RightColumn) && cellGroup.Any(x => x.tag == TagNames.LeftColumn))
            {
                foreach (var cell in cellGroup)
                {
                    Vector2 cellPosition = cell.position;
                    if (cell.tag == TagNames.LeftColumn || cell.tag == TagNames.RightColumn)
                    {
                        cell.transform.parent = null;
                    }
                    cell.GetComponent<BlockController>().Kill();
                    allCells.Remove(cell);
                    MarkFlyingCell(allCells, cellPosition);
                    nbRemoved++;
                }
            }
        }
        return nbRemoved;
    }

    /// <summary>
    /// Mark cells on top of the cellPosition that are floating in the air.
    /// </summary>
    /// <param name="allCells"></param>
    /// <param name="cellPosition">starting position where cells are searched for</param>
    private void MarkFlyingCell(List<Transform> allCells, Vector2 cellPosition)
    {
        List<Transform> upperCell = allCells.Where(c => cellPosition.y < c.position.y && MathExtensions.SafeComparison(cellPosition.x, c.position.x)).OrderBy(c => c.position.y).ToList();
        if (upperCell.Count > 0 && upperCell[0].position.y > 1)
        {
            upperCell.ForEach(c => { c.GetComponent<BlockAnnotation>().UpInTheAir = true; c.GetComponent<BlockController>().ChangeText("L"); });
        }
    }

    #endregion
    #region Compute Flying cells
    /// <summary>
    /// Compute where the flying cell will fall.
    /// </summary>
    /// <param name="allCells"></param>
    /// <returns></returns>
    private List<Transform> ComputeFlyingCellNewPosition(List<Transform> allCells)
    {
        List<Transform> flyingCells = allCells.Where(x => x.GetComponent<BlockAnnotation>().UpInTheAir).ToList();
        if (flyingCells.Count > 0)
        {
            allCells.Where(x => !x.GetComponent<BlockAnnotation>().UpInTheAir).ToList().ForEach(cell => cell.GetComponent<BlockController>().ChangeText(""));
            allCells.ForEach(cell => cell.GetComponent<BlockAnnotation>().NewPosition = cell.position);

            foreach (var flyingCell in flyingCells)
            {
                while (!allCells.Any(cell => MathExtensions.SafeComparison(flyingCell.GetComponent<BlockAnnotation>().NewPosition.y - 1, cell.GetComponent<BlockAnnotation>().NewPosition.y)
                                    && MathExtensions.SafeComparison(flyingCell.position.x, cell.position.x))
                         && flyingCell.GetComponent<BlockAnnotation>().NewPosition.y > 1)
                {
                    flyingCell.GetComponent<BlockAnnotation>().NewPosition = new Vector2(flyingCell.position.x, flyingCell.GetComponent<BlockAnnotation>().NewPosition.y - 1);
                }
                flyingCell.GetComponent<BlockAnnotation>().UpInTheAir = false;
                flyingCell.GetComponent<BlockController>().ChangeText(flyingCell.GetComponent<BlockAnnotation>().NewPosition.y.ToString());
            }
        }
        return flyingCells;
    }

    /// <summary>
    /// Move the cells from their "flying cell" positions to their position on top of another block, one row at a time (for animation).
    /// </summary>
    /// <param name="cellsToLower">Cells that falls</param>
    /// <returns>If the cells are not in their final position</returns>
    private bool MoveCellToLowerPosition(List<Transform> cellsToLower)
    {
        bool stillHasToMove = false;
        foreach (var cell in cellsToLower)
        {
            if (cell.position != cell.GetComponent<BlockAnnotation>().NewPosition)
            {
                cell.position = new Vector2(cell.position.x, cell.position.y - 1);
                if (cell.position != cell.GetComponent<BlockAnnotation>().NewPosition)
                {
                    stillHasToMove = true;
                }
            }
        }
        return stillHasToMove;
    }
    #endregion

    /// <summary>
    /// End the game and return to the menu.
    /// </summary>
    private void EndGame()
    {
        int scene = SceneManager.GetActiveScene().buildIndex - 1;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }


}
