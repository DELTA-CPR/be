# Game of Tag – ArUco Integration Proposal

## 1. Overview

Game of Tag uses computer vision and ArUco markers to identify players, detect their positions in the camera view and implement AR-based targeting.

Each player carries two physical ArUco markers attached to their clothing using adhesive tape:

- One marker on the chest.
- One marker on the back.

Both markers are registered and associated with the same player account.

The mobile application continuously captures camera frames and processes them locally using two computer vision systems:

- **YOLO:** Detects people and creates bounding boxes around them.
- **OpenCV ArUco:** Detects ArUco markers and identifies their IDs.

Detected ArUco markers are associated with YOLO person bounding boxes based on their positions and overlapping areas. This allows the application to determine which players are visible and where they are located in the camera view.

The application displays player information and a configurable targeting crosshair.

When a player fires, the application checks whether the crosshair is inside a detected person's bounding box and whether the player has pressed the shooting button within a defined time window.

If the conditions are satisfied and the bounding box belongs to an enemy player, the application sends a hit event to the backend.

The backend validates the event, eliminates the target and starts their respawn timer.

Camera frames are processed locally. **The backend receives shooting events and game information, never the complete camera video.**

### Main objectives

- Identify individual players using unique ArUco markers.
- Detect people using YOLO.
- Associate ArUco markers with detected person bounding boxes.
- Identify player teams and nicknames.
- Display player information in Augmented Reality.
- Continuously process camera frames locally.
- Implement AR-based aiming and shooting.
- Use a configurable crosshair with identical settings for all players.
- Detect shooting events using the crosshair position and shooting input timestamp.
- Send hit information to the backend.
- Validate hits on the server.
- Eliminate players after one successful hit.
- Synchronize player states using SignalR.
- Support respawning at designated spawn points.
- Represent physical spawn points using ArUco markers.

### System scope

ArUco markers have two primary purposes:

1. **Player identification:** Markers attached to players identify their accounts.
2. **Spawn point identification:** Markers placed at designated locations identify spawn points.

YOLO is responsible for detecting people in camera frames. It does not identify individual players.

The application combines YOLO bounding boxes with ArUco marker detections to associate detected people with registered player accounts.

All computer vision processing takes place locally on the mobile device.

---

## 2. ArUco Marker System

ArUco is a fiducial marker system supported by OpenCV.

Each marker contains a unique identifier encoded in a black-and-white pattern surrounded by a distinctive border.

The mobile application detects marker IDs and their positions in the camera image.

### 2.1 Player markers

Every player has exactly two physical ArUco markers:

- One attached to the chest.
- One attached to the back.

Both markers must be attached using adhesive tape.

These are the only supported player marker placements.

Each marker has a unique numerical ID and is associated with the same player account.

This allows players to be identified from either the front or the back.

### Marker requirements

Markers should be:

- Printed with high contrast.
- Flat and undamaged.
- Large enough to be detected from the intended distance.
- Clearly visible on the chest or back.
- Properly illuminated during gameplay.

Markers must not be covered by clothing or equipment.

### 2.2 Spawn point markers

ArUco markers are also used to represent designated physical spawn points.

Spawn point markers are registered separately from player markers.

Each spawn point contains:

- Unique marker ID.
- Spawn point ID.
- Spawn point name.
- Associated game session.
- Assigned team, if applicable.

The application recognizes spawn point markers by their registration data.

They must not be interpreted as player markers or included in the shooting process.

### 2.3 Marker dictionary

Recommended dictionary for the initial prototype:

`DICT_4X4_50`

It provides 50 unique marker IDs.

The total number of required IDs must account for both player markers and spawn points:

`(Number of players × 2) + Number of spawn points`

For larger matches, a dictionary with more available IDs should be selected.

---

## 3. Game Administration and Session Setup

One player acts as the game administrator.

The administrator creates the game session, registers players, assigns teams and configures game parameters.

### 3.1 Administrator responsibilities

The administrator can:

- Create a new game session.
- Generate a session QR code and joining code.
- Register players by scanning their ArUco markers.
- Assign nicknames to players.
- Assign players to teams.
- Register spawn points.
- Configure the targeting crosshair.
- Configure respawn settings.
- Start and end the game.
- Manage the current game session.

### 3.2 Creating a game session

1. The administrator creates a new game session.
2. The backend creates the session.
3. The backend generates a joining QR code and a unique session code.
4. Other players scan the QR code or manually enter the session code.
5. Their mobile applications connect to the corresponding session.
6. Each player completes their identity registration.

### 3.3 Registering players

The administrator scans both ArUco markers belonging to each player.

The markers are associated with one player account.

The administrator assigns a nickname and team.

Example:

| Property | Example |
|---|---|
| Player ID | `player_123` |
| Nickname | `Player123` |
| Team ID | `blue` |
| Chest marker ID | `12` |
| Back marker ID | `13` |
| Game session | `game_001` |

The backend stores the association between marker IDs and player accounts.

### 3.4 Linking a player's device

After joining the game session, each player must identify themselves by scanning one of their registered ArUco markers.

The front-facing camera can be used for this process.

The application detects the marker ID and sends it to the backend.

The backend verifies that the marker belongs to a registered player in the current session and associates the mobile device with that player account.

The player is then connected with their registered nickname, team and identity.

### 3.5 Downloading player mappings

At the beginning of the game, each client retrieves the marker mapping table from the backend.

The mapping table contains:

- ArUco marker IDs.
- Associated player IDs.
- Player nicknames.
- Team IDs.
- Marker roles.
- Game session information.

The client stores this information locally for the duration of the game.

This allows the application to identify players without sending every marker detection to the backend.

The mapping table can also contain spawn point information, allowing the application to distinguish player markers from spawn point markers.

---

## 4. Continuous Camera Processing

The mobile application continuously captures and processes camera frames during gameplay.

Camera processing is not limited to the moment when a player fires.

Each frame is processed locally using YOLO and OpenCV ArUco.

### 4.1 YOLO person detection

YOLO is used to detect people in camera frames.

For every detected person, the model creates a bounding box containing their estimated position in the image.

The bounding box represents the visible area of a detected person.

The application uses these bounding boxes to determine where potential targets are located.

### 4.2 ArUco marker detection

OpenCV processes the same camera frames and detects visible ArUco markers.

For each detected marker, the application obtains:

- Marker ID.
- Marker center position.
- Marker corner coordinates.
- Estimated marker position and orientation, if available.

The detected marker ID is matched against the locally stored mapping table.

### 4.3 Associating markers with bounding boxes

After YOLO and ArUco detection, the application associates detected markers with person bounding boxes.

The association is based on the spatial relationship between the marker and the bounding box.

For example, a marker whose center lies inside a person bounding box can be associated with that bounding box.

The system must support the possibility that one bounding box contains multiple player markers or multiple player identities.

Multiple markers belonging to the same player must resolve to a single player account.

Example:

| Bounding box | Detected markers | Associated players |
|---|---|---|
| Box A | 12, 13 | Player123 |
| Box B | 21 | Player456 |
| Box C | 31, 32, 33 | Player789, Player999 |

The relationship between bounding boxes and player accounts is maintained locally.

If a marker cannot be associated with a person reliably, the application should not use it as a valid shooting target.

### 4.4 Continuous processing loop

The following operations repeat while the game is running:

1. Capture a camera frame.
2. Run YOLO person detection.
3. Generate person bounding boxes.
4. Detect ArUco markers in the frame.
5. Match marker IDs with the local mapping table.
6. Associate markers with bounding boxes.
7. Assign player nicknames and team information to the corresponding bounding boxes.
8. Update the AR interface.
9. Evaluate the targeting condition.
10. Repeat with the next camera frame.

All image processing takes place on the mobile device.

Camera footage is never continuously transmitted to the backend.

---

## 5. Player Information and AR Interface

The application displays information about detected players.

Information is associated with YOLO bounding boxes using the detected ArUco marker IDs.

Potential information:

- Player nickname.
- Team name.
- Team color.
- Player status.
- Targeting status.

Example:

- Player: Player123
- Team: Blue Team
- Status: Alive

The application can draw visual elements around detected bounding boxes.

For example:

- Blue outline for Blue Team.
- Red outline for Red Team.
- Player nickname above the bounding box.
- Additional visual indicators for eliminated players.

The interface should make it clear which detected person corresponds to which registered player.

If a bounding box is associated with multiple players, the interface should display all relevant player information or another clear indication that multiple identities are associated with the area.

---

## 6. Crosshair and Targeting System

The mobile application displays a targeting crosshair over the live camera view.

The crosshair is used to determine which detected person can be targeted.

### 6.1 Crosshair configuration

The crosshair size is configurable by the game administrator.

The selected configuration must be identical for all players.

Individual players cannot independently change their targeting settings.

The administrator configures the crosshair before starting the match.

The crosshair configuration is distributed through the backend and applied by all connected clients.

### 6.2 Target detection

The application checks whether the crosshair is positioned inside a detected person bounding box.

The targeting condition is evaluated using the current camera frame and its associated detection results.

A bounding box becomes a potential target when the crosshair satisfies the configured targeting condition.

The exact geometric check should be defined consistently across all clients.

### 6.3 Target classification

After determining that the crosshair is inside a bounding box, the application checks the player identities associated with that box.

The application identifies whether the associated player or players belong to an enemy team.

Only enemy players can be eliminated.

Friendly fire is disabled.

If the bounding box belongs only to teammates, the application does not generate a hit event.

### 6.4 Multiple players in one bounding box

One bounding box may contain multiple registered player identities.

In this case, all associated player IDs must be considered during target evaluation.

The application filters out:

- The shooting player.
- Players belonging to the same team.
- Players without a valid registration.
- Players already known to be eliminated.

All remaining enemy player IDs can be included in one shooting request.

Multiple ArUco markers belonging to the same player must never produce duplicate elimination events.

---

## 7. Shooting Mechanism

### 7.1 Basic principle

The application continuously processes camera frames and maintains the latest detected person bounding boxes and player associations.

When a player presses the shooting button, the application records the exact input timestamp.

The application then checks whether the crosshair is inside a valid enemy bounding box and whether the shooting input occurred within the configured time window.

If both conditions are satisfied, the application creates a hit event and sends it to the backend.

The application does not capture a new photograph specifically for every shot.

### 7.2 Shooting controls

A player can shoot using a dedicated shooting button.

The application records the shooting input timestamp locally.

Example:

`firePressedAt = currentTimestamp`

### 7.3 Shooting time window

The application checks whether the shooting button was pressed within the last `N` milliseconds.

Conceptually:

`currentTimestamp - firePressedAt <= N`

The value `N` determines how long a shooting input remains valid for hit detection.

This allows the application to account for small delays between camera processing, target detection and user input.

The value should be configurable during development and tested under real gameplay conditions.

### 7.4 Shooting condition

A hit is generated when both conditions are satisfied:

1. The crosshair is inside an enemy player's bounding box.
2. The shooting button was pressed within the last `N` milliseconds.

The application also checks that the same shooting input has not already generated a hit event.

One shooting input must not create repeated hit requests across multiple camera frames.

### 7.5 Shooting process

1. The player aims using the mobile device camera.
2. The application continuously detects people using YOLO.
3. OpenCV detects ArUco markers.
4. Player identities are associated with bounding boxes.
5. The application displays the targeting crosshair.
6. The player presses the shooting button.
7. The application records the shooting timestamp.
8. The application checks whether the crosshair is inside a detected bounding box.
9. The application checks whether the shooting input occurred within the last `N` milliseconds.
10. The application checks whether the bounding box belongs to an enemy player.
11. The application resolves all valid enemy player IDs associated with the bounding box.
12. The application sends a hit event to the backend.
13. The backend validates the event.
14. If accepted, the target player or players are eliminated.

If no valid enemy target is found, the shot misses.

---

## 8. Hit Communication

The mobile application communicates shooting events to the backend using SignalR.

The application does not send camera frames or video footage.

Only relevant hit information is transmitted.

### 8.1 Shooting request

A shooting request may contain:

- Game session ID.
- Shooter ID.
- Target player IDs.
- Target marker IDs.
- Shot timestamp.
- Unique shot request ID.

Example logical request:

```json
{
  "sessionId": "game_001",
  "shooterId": "player_123",
  "targetPlayerIds": [
    "player_456",
    "player_789"
  ],
  "targetMarkerIds": [
    21,
    31
  ],
  "shotId": "shot_000123",
  "timestamp": "2026-11-18T10:30:00Z"
}
```

The shooter identity should preferably be derived from the authenticated SignalR connection rather than trusted solely from a client-provided ID.

The backend uses the target player IDs and marker IDs to validate the reported targets.

### 8.2 Communication rules

- Shooting requests are sent only when a valid enemy target is detected.
- One shooting input must not generate duplicate requests.
- A shooting request can contain multiple target player IDs.
- The backend independently validates every target.
- The backend is authoritative for elimination and respawn state.
- No camera video is transmitted.

---

## 9. Backend Hit Validation

The backend receives shooting events and determines whether the reported hits are valid.

The backend is responsible for all authoritative player state changes.

### 9.1 Validation process

When a shooting request arrives, the backend checks:

- Whether the game session exists and is active.
- Whether the shooter is registered in the current session.
- Whether the shooter is alive.
- Whether the target player exists in the current session.
- Whether the target is alive.
- Whether the shooter and target belong to different teams.
- Whether the target marker is registered to the reported player.
- Whether the shooting request is valid.
- Whether the shooting request has already been processed.

Both the shooter and the target must be alive for the hit to be accepted.

Each target in a multi-target shooting request is validated independently.

### 9.2 Processing a valid hit

After successful validation:

1. The backend marks the target player as hit.
2. The backend updates the player's state in the database.
3. The backend records the hit timestamp.
4. The backend calculates the respawn timestamp.
5. The backend starts the respawn waiting period.
6. The backend ignores additional shooting requests from the eliminated player until their respawn period ends.
7. The backend sends a notification to the eliminated player.

Example database information:

| Property | Example |
|---|---|
| Player ID | `player_456` |
| Status | `respawning` |
| Hit timestamp | `2026-11-18T10:30:00Z` |
| Respawn timestamp | `2026-11-18T10:35:00Z` |

The server uses its own clock to determine whether the respawn period has expired.

### 9.3 Invalid hits

A shooting event is rejected when:

- The shooter is eliminated.
- The target is already eliminated.
- The shooter and target are on the same team.
- The target marker is not registered to the reported player.
- The game session is inactive.
- The request is duplicated or otherwise invalid.

Rejected requests must not change player states.

---

## 10. Respawning

Players are eliminated after one successful hit.

There are no health points or damage calculations.

After elimination, the backend starts a respawn timer.

### 10.1 Respawn process

1. The player is hit by an enemy.
2. The backend marks the player as hit.
3. The player's status changes to `respawning`.
4. The backend stores the hit timestamp.
5. The backend calculates and stores the respawn timestamp.
6. The backend sends a SignalR notification to the eliminated player.
7. The player cannot shoot while their status is `respawning`.
8. When the respawn timestamp is reached, the backend restores the player's active status.
9. The player can return to normal gameplay.

### 10.2 Respawn notification

The backend sends a SignalR message to the eliminated player's mobile application.

The notification contains the expected respawn timestamp.

Example:

```json
{
  "event": "PlayerHit",
  "playerId": "player_456",
  "status": "respawning",
  "hitAt": "2026-11-18T10:30:00Z",
  "respawnAt": "2026-11-18T10:35:00Z"
}
```

The client can display a countdown using the received timestamp.

The backend remains responsible for determining when the player can return to the game.

### 10.3 Spawn points

ArUco markers also identify designated physical spawn points.

They are registered separately from player markers and can be assigned to specific teams.

The current hit communication flow controls respawning using a server-side timer.

The diagram does not yet define a requirement for verifying that a player has physically returned to their spawn point before the timer expires.

If physical spawn point verification is required, the application must additionally detect the corresponding spawn point marker and send a respawn location confirmation to the backend.

---

## 11. Multiplayer Synchronization

SignalR is used for real-time communication between the backend and connected mobile applications.

The backend distributes important game events to the relevant clients.

### 11.1 Notification to eliminated player

When a player is hit, the backend sends a notification to that player's mobile application.

The notification contains:

- Player ID.
- Current status.
- Hit timestamp.
- Respawn timestamp.

This allows the eliminated player to see their current status and respawn countdown.

### 11.2 Notification to other players

The backend can also notify other connected players about elimination events.

Potential uses:

- Updating player status.
- Updating team information.
- Updating a leaderboard.
- Displaying elimination notifications.

The exact visibility of elimination information can depend on the game mode.

### 11.3 Respawn synchronization

When a player's respawn period ends, the backend updates their state to `alive`.

The change is synchronized with connected clients using SignalR.

Other players can then recognize the player as an active participant again.

---

## 12. Player States

The backend maintains authoritative player states.

| State | Description |
|---|---|
| `alive` | Player can participate in combat |
| `respawning` | Player has been hit and cannot participate until the respawn timestamp |
| `protected` | Player is temporarily protected by a spawn point or safe zone |

A player in the `respawning` state cannot generate valid shooting actions.

When the server-side respawn timer expires, the backend restores the player's `alive` status.

---

## 13. Game Configuration

The administrator can configure game parameters before starting the match.

Settings are stored by the backend and distributed to all connected clients.

### Configurable parameters

- Crosshair size.
- Shooting input validity window (`N` milliseconds).
- Respawn duration.
- Team assignments.
- Spawn point assignments.
- Match duration, if applicable.

### Global configuration rules

The crosshair size must be identical for all players.

Individual players cannot change their own targeting configuration.

The selected settings are distributed to all clients when the game starts.

The backend is the authoritative source of game configuration.

---

## 14. Technical Architecture

### Proposed technologies

| Component | Technology |
|---|---|
| Mobile application | Unity |
| AR rendering | AR Foundation |
| Camera processing | OpenCV |
| ArUco detection | OpenCV ArUco |
| Person detection | YOLO |
| Backend | ASP.NET Core |
| Multiplayer communication | SignalR |
| Database | PostgreSQL |

### 14.1 Client-side camera processing

Mobile Camera  
↓  
Continuous Camera Frame Capture  
↓  
YOLO Person Detection  
↓  
Generate Person Bounding Boxes  
↓  
OpenCV ArUco Detection  
↓  
Identify Marker IDs and Positions  
↓  
Load Player and Team Mappings  
↓  
Associate Markers with Bounding Boxes  
↓  
Assign Player Information to Bounding Boxes  
↓  
Display AR Information  
↓  
Check Crosshair Position and Shooting Input  
↓  
Check Whether Bounding Box Belongs to Enemy Player(s)  
↓  
Create Hit Event  
↓  
Send Hit Information to Backend

This process repeats while the game is running.

### 14.2 Backend hit processing

SignalR Shooting Message  
↓  
Receive Shooter and Target Information  
↓  
Validate Game Session  
↓  
Check Whether Shooter Is Alive  
↓  
Check Whether Target(s) Are Alive  
↓  
Validate Team Membership and Marker Ownership  
↓  
Reject Invalid or Duplicate Requests  
↓  
Mark Valid Target(s) as Hit in Database  
↓  
Calculate Respawn Timestamp  
↓  
Start Server-side Respawn Timer  
↓  
Ignore Further Hit Messages from Eliminated Players  
↓  
Send SignalR Notification to Eliminated Player  
↓  
Synchronize Relevant Player State Changes

### 14.3 Network architecture

The system separates computer vision processing from multiplayer communication.

**Mobile application:**

- Captures camera frames continuously.
- Runs YOLO person detection.
- Detects ArUco markers.
- Associates markers with bounding boxes.
- Determines potential hits.
- Sends shooting events.

**Backend:**

- Stores player and marker mappings.
- Manages game sessions.
- Manages teams.
- Validates shooting requests.
- Determines player eliminations.
- Manages respawn timers.
- Synchronizes player states.

The backend never needs to receive or process the camera video.

---

## 15. Limitations and Considerations

| Limitation | Possible solution |
|---|---|
| Player markers must remain visible | Use one marker on the chest and one on the back |
| Markers can be obstructed | Ensure both markers remain clearly visible |
| Poor lighting affects detection | Ensure sufficient illumination |
| YOLO may fail to detect a person | Optimize the model and camera resolution |
| ArUco markers may be associated with the wrong bounding box | Use marker position and bounding box overlap checks |
| One bounding box may contain multiple player markers | Support multiple player associations and deduplicate player IDs |
| Multiple people may overlap in the camera image | Improve detection and avoid ambiguous target associations |
| Detection accuracy decreases with distance | Optimize marker dimensions and YOLO input resolution |
| Continuous image processing affects performance | Optimize frame resolution and detection frequency |
| Marker IDs can be copied | Use server-side marker ownership validation |
| Client detection cannot prove a physical hit | Use server-side validation and additional anti-cheat measures |
| Backend cannot independently verify camera footage | Treat hit information as a client-reported game event |
| Network connectivity may be interrupted | Handle reconnections and synchronize player states |
| Mobile camera processing may introduce latency | Keep hit detection and image processing local |

### Important distinctions

- YOLO detects people but does not identify their accounts.
- ArUco markers identify players but do not independently detect complete person bounding boxes.
- The application associates ArUco detections with YOLO bounding boxes.
- A bounding box can contain multiple registered player identities.
- The application continuously processes camera frames locally.
- Camera video is never transmitted to the backend.
- The crosshair must be inside an enemy bounding box.
- A shooting input must occur within the last `N` milliseconds.
- Friendly fire is disabled.
- One successful hit immediately eliminates the target.
- The backend validates hits and manages player states.
- SignalR delivers elimination and respawn notifications.
- Spawn point markers are registered separately from player markers.

---

## 16. Development Plan

### Phase 1 – Camera and ArUco Prototype

- Integrate OpenCV into the Unity mobile application.
- Access the mobile device camera.
- Implement continuous camera frame capture.
- Detect ArUco markers.
- Identify marker IDs.
- Estimate marker positions.
- Test markers attached to clothing.

### Phase 2 – YOLO Person Detection

- Integrate a YOLO model.
- Detect people in camera frames.
- Generate person bounding boxes.
- Optimize person detection performance.
- Test detection with multiple people in the camera view.

### Phase 3 – Player Identification

- Implement player registration.
- Associate two marker IDs with each player.
- Implement nickname assignment.
- Implement team assignment.
- Load marker mappings from the backend.
- Associate detected markers with person bounding boxes.
- Support multiple player associations per bounding box.
- Display player information in AR.

### Phase 4 – Game Session Management

- Implement game session creation.
- Generate session QR codes and joining codes.
- Implement player joining.
- Implement administrator permissions.
- Implement player registration through scanning.
- Implement player self-identification.
- Link mobile devices with player accounts.
- Register spawn points.
- Implement global game configuration.

### Phase 5 – Targeting and Shooting

- Implement the targeting crosshair.
- Implement configurable crosshair size.
- Synchronize crosshair settings across clients.
- Implement shooting controls.
- Record shooting timestamps.
- Implement the `N` millisecond shooting window.
- Detect crosshair overlap with person bounding boxes.
- Identify enemy players.
- Handle multiple players associated with one bounding box.
- Prevent repeated hit events from one shooting input.

### Phase 6 – Backend Hit Processing

- Implement SignalR shooting messages.
- Implement shooting request validation.
- Validate shooter and target states.
- Validate team membership.
- Validate marker ownership.
- Prevent duplicate shooting requests.
- Store eliminations in the database.
- Implement server-side respawn timers.
- Ignore hit messages from eliminated players.
- Send elimination notifications to affected players.

### Phase 7 – Multiplayer and Respawning

- Synchronize player states.
- Synchronize eliminations.
- Implement respawn notifications.
- Implement respawn countdowns.
- Implement spawn point management.
- Implement optional leaderboard notifications.
- Handle network reconnections.
- Test complete multiplayer gameplay.

---

## 17. Expected Result

The final system allows players to participate in a mobile AR laser-tag game using their mobile device cameras.

One player acts as the administrator and creates the game session. Other players join using a QR code or session code.

The administrator registers players by scanning their two ArUco markers, assigns nicknames and organizes teams.

At the beginning of the game, all clients download the player and marker mapping table from the backend.

During gameplay, the mobile application continuously captures camera frames and processes them locally.

YOLO detects people and generates bounding boxes. OpenCV detects ArUco markers and identifies their IDs. The application associates marker detections with the corresponding person bounding boxes and assigns player information to them.

The application displays the detected players and a configurable crosshair. All players use the same crosshair configuration.

When a player fires, the application checks whether the crosshair is inside an enemy bounding box and whether the shooting button was pressed within the last `N` milliseconds.

If a valid target is found, the application sends the corresponding player ID or IDs to the backend.

The backend validates the shooting event and checks whether both the shooter and targets are alive. If the request is accepted, the target players are immediately eliminated.

There are no health points. One successful hit equals one elimination.

The backend stores the elimination, starts the respawn timer and sends the eliminated player a SignalR notification containing their respawn timestamp.

Eliminated players cannot shoot until their respawn period ends.

ArUco markers are also used to identify physical spawn points.

The system combines YOLO person detection, ArUco player identification, local computer vision processing, Augmented Reality and real-time multiplayer communication to create a mobile AR laser-tag experience without transmitting camera video to the backend.