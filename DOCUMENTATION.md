# Quick Run-down

### Config Editor

Here, the game strategy can be adjusted. The settings are stored in *profiles*, which can be saved and loaded. The options are:

- Explore costs
  
  - these numbers are used by the exploration algorithm (there are 2 modes, "closest" and "closest base")
  
  - "closest base" is used at the start of the match, when the robot doesn't have a battery
    
    - "closest base - player" is the cost of exploring a new tile, multiplied by the distance to the robot
    
    - "closest base - base" is the cost of exploring a new tile, multiplied by the distance to the base (matters because the robot needs to return to base to get the battery)
  
  - "closest player" is used after the battery has been obtained. The cost to explore a tile is calculated in the same way as "closest base" 

- Tile Costs
  
  - these numbers are used by the pathfinding algorithm
  
  - the robot always chooses to mine the most precious ore it has knowledge of
  
  - these costs can softly guide it to pick up other ores or take different paths (e.g. through unknown space), if adjusted correctly

- Upgrade List
  
  - this is the queue of upgrades to do

- Player override cost
  
  - when a player is sighted, the robot will avoid that area. This cost is added to all tiles in the vicinity of the enemy player, which will "bend" the paths around that area. If an ore is known to exist in that area, the robot will still try to obtain it

- Reserve osmium
  
  - this amount of osmium is kept in reserve and not used for upgrades. Meant to be used for repairing the robot after battles

- Rounds margin
  
  - the robot will start heading towards the center of the map in advance, by this number of rounds

- *Special settings*
  
  - "Use dimensional rift" - if checked, the robot will try to scan the memory of the game to obtain the map
  
  - "Decipher universe" - if checked, the robot will try to decipher the seed of the map



### Main Screen

There are multiple dialogs which contain information about the match:

- Game
  
  - shows the upgrades that have been done
  
  - shows the current level of each system
  
  - shows the inventory of the robot
  
  - shows the HP of the robot

- Damage log
  
  - lists the attacks received by the robot

- Logs
  
  - lists various events
  
  - includes each player sighting
  
  - 






