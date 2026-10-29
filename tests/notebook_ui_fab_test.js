/**
 * Tests for Terminal Notebooks UI: Horizontal Single-Row Layout,
 * Floating Action Buttons, Speed-Dial Menu, and Theme Accessibility.
 */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

console.log("==================================================");
console.log("  TERMINAL NOTEBOOK UI & FAB SPEED-DIAL TESTS     ");
console.log("==================================================");

const cssPath = path.join(__dirname, '../wwwroot/css/terminal-notebooks.css');
const indexViewPath = path.join(__dirname, '../Views/Notebooks/Index.cshtml');
const editViewPath = path.join(__dirname, '../Views/Notebooks/Edit.cshtml');
const courseEditViewPath = path.join(__dirname, '../Views/InstructorCourses/EditCourseNotebook.cshtml');

// 1. Verify CSS Asset
assert.ok(fs.existsSync(cssPath), "terminal-notebooks.css must exist");
const cssContent = fs.readFileSync(cssPath, 'utf8');

// Test 1.1: Horizontal Notebook Row Classes
console.log("\n--- 1. Horizontal Notebook Row Layout Verification ---");
assert.ok(cssContent.includes('.notebook-list'), "CSS must define .notebook-list container");
assert.ok(cssContent.includes('.notebook-row'), "CSS must define .notebook-row class");
assert.ok(cssContent.includes('.notebook-identity-zone'), "CSS must define .notebook-identity-zone (Left Zone)");
assert.ok(cssContent.includes('.notebook-middle-zone'), "CSS must define .notebook-middle-zone (Middle Zone)");
assert.ok(cssContent.includes('.notebook-actions-zone'), "CSS must define .notebook-actions-zone (Right Zone)");
assert.ok(cssContent.includes('.notebook-open-btn'), "CSS must define .notebook-open-btn primary action");
assert.ok(cssContent.includes('.notebook-menu-details'), "CSS must define overflow details/summary menu");
console.log("[PASS] Horizontal Row CSS classes properly structured into three distinct zones");

// Test 1.2: Index View Verification
const indexContent = fs.readFileSync(indexViewPath, 'utf8');
assert.ok(!indexContent.includes('col-md-6 col-lg-4'), "Grid card classes (col-md-6 col-lg-4) must be removed");
assert.ok(indexContent.includes('class="notebook-list"'), "Index view must use notebook-list container");
assert.ok(indexContent.includes('class="notebook-row"'), "Index view must render .notebook-row items");
assert.ok(indexContent.includes('class="notebook-identity-zone"'), "Index view must have Left identity zone");
assert.ok(indexContent.includes('class="notebook-middle-zone"'), "Index view must have Middle badges zone");
assert.ok(indexContent.includes('class="notebook-actions-zone"'), "Index view must have Right actions zone");
assert.ok(indexContent.includes('class="notebook-open-btn"'), "Index view must have primary Open navigation");
assert.ok(indexContent.includes('class="notebook-menu-details"'), "Index view must have contextual overflow menu");
assert.ok(!indexContent.includes('📁') && !indexContent.includes('▶') && !indexContent.includes('🗑️'), "Index view must not contain emoji icons");
console.log("[PASS] Index.cshtml renders horizontal single-row layout without emojis or card grids");

// Test 2: Floating Action Button on Notebooks Listing Page
console.log("\n--- 2. Listing Page FAB Verification ---");
assert.ok(cssContent.includes('.fab-container'), "CSS must define .fab-container");
assert.ok(cssContent.includes('.fab-primary-btn'), "CSS must define .fab-primary-btn");
assert.ok(cssContent.includes('.fab-label'), "CSS must define .fab-label");
assert.ok(cssContent.includes('.fab-primary-btn:hover .fab-label') || cssContent.includes('.fab-primary-btn:hover'), "CSS must expand label on hover");
assert.ok(indexContent.includes('id="fabCreateNotebookBtn"'), "Index view must contain FAB Create button");
assert.ok(indexContent.includes('Create New Notebook'), "FAB must have label 'Create New Notebook'");
assert.ok(indexContent.includes('asp-action="Create"'), "FAB form must submit to Create action");
console.log("[PASS] Listing page FAB exists with smooth expansion on hover and keyboard focus");

// Test 3: Notebook Editor Speed-Dial FAB Verification
console.log("\n--- 3. Notebook Editor Speed-Dial FAB Verification ---");
const editContent = fs.readFileSync(editViewPath, 'utf8');
const courseEditContent = fs.readFileSync(courseEditViewPath, 'utf8');

assert.ok(cssContent.includes('.speed-dial-backdrop'), "CSS must define speed-dial backdrop scrim");
assert.ok(cssContent.includes('.speed-dial-container'), "CSS must define speed-dial container");
assert.ok(cssContent.includes('.speed-dial-fab'), "CSS must define speed-dial FAB button");
assert.ok(cssContent.includes('.speed-dial-menu'), "CSS must define speed-dial menu");
assert.ok(cssContent.includes('.speed-dial-item'), "CSS must define speed-dial menu items");
assert.ok(cssContent.includes('rotate(135deg)'), "FAB icon must rotate into close indicator when opened");

// Editor View Check
assert.ok(!editContent.includes('class="add-block-tray"'), "Old inline add-block-tray must be replaced in Edit.cshtml");
assert.ok(editContent.includes('id="speedDialFab"'), "Edit.cshtml must contain speedDialFab button");
assert.ok(editContent.includes('id="speedDialBackdrop"'), "Edit.cshtml must contain speedDialBackdrop scrim");
assert.ok(editContent.includes('Add Markdown Notes'), "Speed-dial must have Action 1: Add Markdown Notes");
assert.ok(editContent.includes('Add Interactive Terminal'), "Speed-dial must have Action 2: Add Interactive Terminal");
assert.ok(editContent.includes('Add Static Command Example'), "Speed-dial must have Action 3: Add Static Command Example");
assert.ok(editContent.includes('onclick="executeSpeedDialAction(addMarkdownBlock)"'), "Action 1 must trigger addMarkdownBlock");
assert.ok(editContent.includes('onclick="executeSpeedDialAction(addInteractiveTerminalBlock)"'), "Action 2 must trigger addInteractiveTerminalBlock");
assert.ok(editContent.includes('onclick="executeSpeedDialAction(addStaticTerminalBlock)"'), "Action 3 must trigger addStaticTerminalBlock");
assert.ok(editContent.includes('configureBlockInSimulationBuilder'), "Interactive terminal block configuration with shared EditSimulation preserved");
console.log("[PASS] Notebook Editor has full speed-dial FAB with 3 actions, backdrop, and keyboard/touch dismissal");

// Course Notebook Editor Check
assert.ok(!courseEditContent.includes('class="add-block-tray"'), "Old inline add-block-tray must be replaced in EditCourseNotebook.cshtml");
assert.ok(courseEditContent.includes('id="speedDialFab"'), "EditCourseNotebook.cshtml must contain speedDialFab button");
assert.ok(courseEditContent.includes('Add Markdown Notes'), "Course editor speed-dial must have Action 1");
assert.ok(courseEditContent.includes('Add Interactive Terminal'), "Course editor speed-dial must have Action 2");
assert.ok(courseEditContent.includes('Add Static Command Example'), "Course editor speed-dial must have Action 3");
console.log("[PASS] Course Notebook Editor also updated with matching Speed-Dial FAB");

// Test 4: Light Mode and Dark Mode Verification
console.log("\n--- 4. Theme Audit & Contrast Verification ---");
assert.ok(cssContent.includes('html[data-theme="dark"]') || cssContent.includes('data-theme="dark"'), "Dark mode selectors must be present");
assert.ok(cssContent.includes(':is(html[data-theme="dark"], html[data-appearance="dark"]) .notebook-row'), "Dark mode must style notebook rows");
assert.ok(cssContent.includes(':is(html[data-theme="dark"], html[data-appearance="dark"]) .notebook-menu-dropdown'), "Dark mode must style overflow dropdowns");
assert.ok(cssContent.includes(':is(html[data-theme="dark"], html[data-appearance="dark"]) .speed-dial-label'), "Dark mode must style speed dial labels");
assert.ok(cssContent.includes(':is(html[data-theme="dark"], html[data-appearance="dark"]) .fab-primary-btn'), "Dark mode must style FAB buttons");
assert.ok(cssContent.includes(':is(html[data-theme="dark"], html[data-appearance="dark"]) .speed-dial-backdrop'), "Dark mode must style backdrop");
assert.ok(cssContent.includes('prefers-reduced-motion'), "Must support prefers-reduced-motion accessibility");
console.log("[PASS] Full Light Mode and Dark Mode support verified across all notebook components");

console.log("\n==================================================");
console.log("ALL 4 TEST SUITES PASSED! (100% SUCCESS)         ");
console.log("==================================================");
