Allows people to play through steam with 4+ players, it's currently fixed at 8 but nowhere in the code should this be assumed, this can be increased in the Plugin constructor. 
Tested with version 2.3.3

# Known Issues
- Score can desync, not sure why, not sure how. Works most of the time tho 🤷‍♂️
- Limited to 4 teams still, can be fixed but requires creating a system for more spawns.

# Improvements to be made
The packet system is a bit... questionable. A library to add custom packets to abstract this away would improve stability across updates. If a bopl update adds a packet with an unlucky size it breaks the mod.
