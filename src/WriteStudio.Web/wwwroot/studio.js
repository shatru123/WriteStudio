// WriteStudio Interactive Web Studio Client Engine
// Complete with Vector Drawing, Live Webcam PiP Compositing, MP4 Local Recording, and IndexedDB Video Library

// ==========================================
// IndexedDB Local Storage Manager
// ==========================================
class WriteStudioStorage {
    constructor() {
        this.dbName = 'WriteStudioDB';
        this.dbVersion = 1;
        this.db = null;
    }

    async init() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this.dbName, this.dbVersion);

            request.onupgradeneeded = (e) => {
                const db = e.target.result;
                if (!db.objectStoreNames.contains('recordings')) {
                    const store = db.createObjectStore('recordings', { keyPath: 'id' });
                    store.createIndex('createdAt', 'createdAt', { unique: false });
                }
                if (!db.objectStoreNames.contains('drafts')) {
                    db.createObjectStore('drafts', { keyPath: 'id' });
                }
            };

            request.onsuccess = (e) => {
                this.db = e.target.result;
                resolve(this.db);
            };

            request.onerror = (e) => reject(e.target.error);
        });
    }

    async saveRecording(recording) {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readwrite');
            const store = tx.objectStore('recordings');
            const req = store.put(recording);
            req.onsuccess = () => resolve(recording);
            req.onerror = (e) => reject(e.target.error);
        });
    }

    async getAllRecordings() {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readonly');
            const store = tx.objectStore('recordings');
            const req = store.getAll();
            req.onsuccess = () => {
                const results = req.result || [];
                results.sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
                resolve(results);
            };
            req.onerror = (e) => reject(e.target.error);
        });
    }

    async getRecording(id) {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readonly');
            const store = tx.objectStore('recordings');
            const req = store.get(id);
            req.onsuccess = () => resolve(req.result);
            req.onerror = (e) => reject(e.target.error);
        });
    }

    async deleteRecording(id) {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readwrite');
            const store = tx.objectStore('recordings');
            const req = store.delete(id);
            req.onsuccess = () => resolve();
            req.onerror = (e) => reject(e.target.error);
        });
    }

    async clearAllRecordings() {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readwrite');
            const store = tx.objectStore('recordings');
            const req = store.clear();
            req.onsuccess = () => resolve();
            req.onerror = (e) => reject(e.target.error);
        });
    }
}

// ==========================================
// Main WriteStudio Application Engine
// ==========================================
class WriteStudioEngine {
    constructor() {
        this.storage = new WriteStudioStorage();
        this.canvas = document.getElementById('whiteboardCanvas');
        this.ctx = this.canvas.getContext('2d');
        this.canvasWrapper = document.getElementById('canvasWrapper');

        // Studio State
        this.activeTool = 'pen';
        this.activeColor = '#FFFFFF';
        this.activeThickness = 4;
        this.activeBackground = 'Blackboard';
        this.currentPageIndex = 0;
        this.pages = [
            { index: 0, title: 'Page 1', background: 'Blackboard', strokes: [] }
        ];
        this.undoStack = [];
        this.redoStack = [];

        // In-flight pointer state
        this.isPointerDown = false;
        this.currentStroke = null;
        this.shapeStartPoint = null;

        // Recording & Timeline State
        this.recordingState = 'Stopped';
        this.sessionStartTime = null;
        this.pauseStartTime = null;
        this.totalPausedDuration = 0;
        this.timelineEvents = [];
        this.timerInterval = null;
        this.animFrameId = null;

        // Presenter Reference Slides
        this.slides = [];
        this.currentSlideIndex = -1;

        // Hardware & Media Streams
        this.audioContext = null;
        this.analyserNode = null;
        this.audioStream = null;
        this.cameraStream = null;
        
        // Media Recorders
        this.audioRecorder = null;
        this.audioChunks = [];
        this.recordedAudioBlob = null;

        this.cameraRecorder = null;
        this.cameraChunks = [];
        this.recordedCameraBlob = null;

        // Combined in-browser local canvas + camera recorder
        this.canvasRecorder = null;
        this.canvasChunks = [];
        this.localVideoBlob = null;

        this.cameraLayout = {
            preset: 'BottomRight',
            isMirrored: true,
            isVisible: true
        };

        this.init();
    }

    async init() {
        await this.storage.init();
        this.setupCanvasSize();
        window.addEventListener('resize', () => this.setupCanvasSize());

        this.bindCanvasEvents();
        this.bindToolEvents();
        this.bindSlideEvents();
        this.bindMediaEvents();
        this.bindRecordingEvents();
        this.bindExportEvents();
        this.bindLibraryEvents();
        this.bindMobileDrawerEvents();
        this.bindResizerEvents();

        this.renderCanvas();
        this.updateLibraryBadge();
        this.startContinuousRenderLoop();
    }

    setupCanvasSize() {
        const rect = this.canvasWrapper.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        this.canvas.width = rect.width * dpr;
        this.canvas.height = rect.height * dpr;
        this.ctx.scale(dpr, dpr);
        this.cssWidth = rect.width;
        this.cssHeight = rect.height;
        this.renderCanvas();
    }

    get currentPage() {
        return this.pages[this.currentPageIndex] || this.pages[0];
    }

    startContinuousRenderLoop() {
        const renderLoop = () => {
            if (this.recordingState === 'Recording' || (this.cameraStream && this.cameraLayout.isVisible && this.cameraLayout.preset !== 'Hidden')) {
                this.renderCanvas();
            }
            requestAnimationFrame(renderLoop);
        };
        requestAnimationFrame(renderLoop);
    }

    // ==========================================
    // Canvas Drawing & Pointer Events
    // ==========================================
    bindCanvasEvents() {
        this.canvas.addEventListener('pointerdown', (e) => this.onPointerDown(e));
        this.canvas.addEventListener('pointermove', (e) => this.onPointerMove(e));
        this.canvas.addEventListener('pointerup', (e) => this.onPointerUp(e));
        this.canvas.addEventListener('pointercancel', (e) => this.onPointerUp(e));
    }

    getCanvasPos(e) {
        const rect = this.canvas.getBoundingClientRect();
        return {
            x: (e.clientX - rect.left) * (1920 / this.cssWidth),
            y: (e.clientY - rect.top) * (1080 / this.cssHeight),
            pressure: e.pressure && e.pressure > 0 ? e.pressure : 0.5
        };
    }

    onPointerDown(e) {
        this.canvas.setPointerCapture(e.pointerId);
        this.isPointerDown = true;
        const pos = this.getCanvasPos(e);
        const timestamp = this.getElapsedSessionTime();

        if (this.activeTool === 'eraser') {
            this.eraseAt(pos.x, pos.y, this.activeThickness * 4);
            return;
        }

        if (this.isShapeTool(this.activeTool)) {
            this.shapeStartPoint = pos;
            return;
        }

        if (this.activeTool === 'text') {
            const text = prompt('Enter annotation text:');
            if (text) {
                const textStroke = {
                    id: this.generateGuid(),
                    pageIndex: this.currentPageIndex,
                    toolType: 'Text',
                    color: this.hexToRgba(this.activeColor),
                    thickness: this.activeThickness,
                    opacity: 1.0,
                    textContent: text,
                    fontSize: Math.max(18, this.activeThickness * 6),
                    points: [{ x: pos.x, y: pos.y, pressure: 1.0, timestamp: timestamp }],
                    startTime: timestamp,
                    endTime: timestamp
                };
                this.addStroke(textStroke);
            }
            this.isPointerDown = false;
            return;
        }

        const strokeColor = this.activeTool === 'highlighter' ? this.hexToRgba('#FFF176', 0.45) : this.hexToRgba(this.activeColor);
        const strokeThickness = this.activeTool === 'highlighter' ? Math.max(this.activeThickness * 3, 20) : this.activeThickness;

        this.currentStroke = {
            id: this.generateGuid(),
            pageIndex: this.currentPageIndex,
            toolType: this.activeTool === 'highlighter' ? 'Highlighter' : (this.activeTool === 'pencil' ? 'Pencil' : 'Pen'),
            color: strokeColor,
            thickness: strokeThickness,
            opacity: this.activeTool === 'highlighter' ? 0.45 : 1.0,
            points: [{ x: pos.x, y: pos.y, pressure: pos.pressure, timestamp: timestamp }],
            startTime: timestamp,
            endTime: timestamp
        };

        if (this.recordingState === 'Recording') {
            this.recordTimelineEvent({
                $eventType: 'StrokeStarted',
                timestamp: timestamp,
                stroke: JSON.parse(JSON.stringify(this.currentStroke))
            });
        }

        this.renderCanvas();
    }

    onPointerMove(e) {
        if (!this.isPointerDown) return;
        const pos = this.getCanvasPos(e);
        const timestamp = this.getElapsedSessionTime();

        if (this.activeTool === 'eraser') {
            this.eraseAt(pos.x, pos.y, this.activeThickness * 4);
            return;
        }

        if (this.shapeStartPoint) {
            this.renderCanvas();
            this.drawShapePreview(this.shapeStartPoint, pos);
            return;
        }

        if (this.currentStroke) {
            const point = { x: pos.x, y: pos.y, pressure: pos.pressure, timestamp: timestamp };
            this.currentStroke.points.push(point);
            this.currentStroke.endTime = timestamp;

            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'StrokePointAdded',
                    timestamp: timestamp,
                    strokeId: this.currentStroke.id,
                    point: point
                });
            }

            this.renderCanvas();
        }
    }

    onPointerUp(e) {
        if (!this.isPointerDown) return;
        this.isPointerDown = false;
        const pos = this.getCanvasPos(e);
        const timestamp = this.getElapsedSessionTime();

        if (this.shapeStartPoint) {
            const points = this.generateShapePoints(this.activeTool, this.shapeStartPoint, pos, timestamp);
            const shapeStroke = {
                id: this.generateGuid(),
                pageIndex: this.currentPageIndex,
                toolType: this.capitalize(this.activeTool),
                color: this.hexToRgba(this.activeColor),
                thickness: this.activeThickness,
                opacity: 1.0,
                points: points,
                startTime: timestamp,
                endTime: timestamp
            };
            this.addStroke(shapeStroke);
            this.shapeStartPoint = null;
            return;
        }

        if (this.currentStroke) {
            this.addStroke(this.currentStroke);
            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'StrokeCompleted',
                    timestamp: timestamp,
                    strokeId: this.currentStroke.id
                });
            }
            this.currentStroke = null;
        }
    }

    isShapeTool(tool) {
        return ['line', 'rectangle', 'circle', 'arrow'].includes(tool);
    }

    generateShapePoints(tool, p1, p2, timestamp) {
        if (tool === 'line') {
            return [
                { x: p1.x, y: p1.y, pressure: 0.5, timestamp: timestamp },
                { x: p2.x, y: p2.y, pressure: 0.5, timestamp: timestamp }
            ];
        }
        if (tool === 'rectangle') {
            const left = Math.min(p1.x, p2.x), right = Math.max(p1.x, p2.x);
            const top = Math.min(p1.y, p2.y), bottom = Math.max(p1.y, p2.y);
            return [
                { x: left, y: top, pressure: 0.5, timestamp: timestamp },
                { x: right, y: top, pressure: 0.5, timestamp: timestamp },
                { x: right, y: bottom, pressure: 0.5, timestamp: timestamp },
                { x: left, y: bottom, pressure: 0.5, timestamp: timestamp },
                { x: left, y: top, pressure: 0.5, timestamp: timestamp }
            ];
        }
        if (tool === 'circle') {
            const cx = (p1.x + p2.x) / 2, cy = (p1.y + p2.y) / 2;
            const rx = Math.abs(p2.x - p1.x) / 2, ry = Math.abs(p2.y - p1.y) / 2;
            const points = [];
            const segments = 32;
            for (let i = 0; i <= segments; i++) {
                const angle = (2 * Math.PI * i) / segments;
                points.push({
                    x: cx + rx * Math.cos(angle),
                    y: cy + ry * Math.sin(angle),
                    pressure: 0.5,
                    timestamp: timestamp
                });
            }
            return points;
        }
        if (tool === 'arrow') {
            const angle = Math.atan2(p2.y - p1.y, p2.x - p1.x);
            const headLen = 24;
            const a1 = angle - Math.PI / 6;
            const a2 = angle + Math.PI / 6;
            return [
                { x: p1.x, y: p1.y, pressure: 0.5, timestamp: timestamp },
                { x: p2.x, y: p2.y, pressure: 0.5, timestamp: timestamp },
                { x: p2.x - headLen * Math.cos(a1), y: p2.y - headLen * Math.sin(a1), pressure: 0.5, timestamp: timestamp },
                { x: p2.x, y: p2.y, pressure: 0.5, timestamp: timestamp },
                { x: p2.x - headLen * Math.cos(a2), y: p2.y - headLen * Math.sin(a2), pressure: 0.5, timestamp: timestamp }
            ];
        }
        return [];
    }

    addStroke(stroke) {
        this.currentPage.strokes.push(stroke);
        this.undoStack.push({ type: 'addStroke', stroke: stroke, pageIndex: this.currentPageIndex });
        this.redoStack = [];
        this.renderCanvas();
    }

    eraseAt(x, y, radius) {
        const page = this.currentPage;
        const remaining = [];
        const erased = [];

        for (const stroke of page.strokes) {
            let hit = false;
            for (const pt of stroke.points) {
                const dist = Math.hypot(pt.x - x, pt.y - y);
                if (dist <= radius + stroke.thickness) {
                    hit = true;
                    break;
                }
            }
            if (hit) {
                erased.push(stroke);
            } else {
                remaining.push(stroke);
            }
        }

        if (erased.length > 0) {
            page.strokes = remaining;
            this.undoStack.push({ type: 'eraseStrokes', strokes: erased, pageIndex: this.currentPageIndex });
            this.redoStack = [];

            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'StrokesErased',
                    timestamp: this.getElapsedSessionTime(),
                    pageIndex: this.currentPageIndex,
                    erasedStrokeIds: erased.map(s => s.id)
                });
            }

            this.renderCanvas();
        }
    }

    // ==========================================
    // Canvas Rendering Loop & Webcam Compositing
    // ==========================================
    renderCanvas() {
        if (!this.ctx) return;

        const scaleX = this.cssWidth / 1920;
        const scaleY = this.cssHeight / 1080;

        this.ctx.save();
        this.ctx.clearRect(0, 0, this.cssWidth, this.cssHeight);

        // 1. Background
        this.drawBackground(this.currentPage.background);

        // 2. Render Page Strokes
        this.ctx.scale(scaleX, scaleY);

        for (const stroke of this.currentPage.strokes) {
            this.drawStroke(stroke);
        }

        if (this.currentStroke) {
            this.drawStroke(this.currentStroke);
        }

        this.ctx.restore();

        // 3. Render Live Moving Presenter Webcam PiP Layer directly onto canvas
        const webcamVideo = document.getElementById('webcamVideo');
        if (this.cameraStream && webcamVideo && webcamVideo.readyState >= 2 && this.cameraLayout.isVisible && this.cameraLayout.preset !== 'Hidden') {
            this.drawWebcamPip(webcamVideo);
        }
    }

    drawWebcamPip(video) {
        const w = 1920;
        const h = 1080;
        const preset = this.cameraLayout.preset || 'BottomRight';
        
        let pipW = 420;
        let pipH = 236; // 16:9 ratio
        let pipX = w - pipW - 36;
        let pipY = h - pipH - 36;

        if (preset === 'BottomLeft') {
            pipX = 36;
            pipY = h - pipH - 36;
        } else if (preset === 'TopRight') {
            pipX = w - pipW - 36;
            pipY = 36;
        } else if (preset === 'TopLeft') {
            pipX = 36;
            pipY = 36;
        } else if (preset === 'Fullscreen') {
            pipX = 0;
            pipY = 0;
            pipW = w;
            pipH = h;
        }

        const scaleX = this.cssWidth / 1920;
        const scaleY = this.cssHeight / 1080;
        const radius = preset === 'Fullscreen' ? 0 : 12;

        this.ctx.save();
        this.ctx.scale(scaleX, scaleY);

        // Clip rounded rectangle for webcam PiP
        this.ctx.beginPath();
        if (this.ctx.roundRect) {
            this.ctx.roundRect(pipX, pipY, pipW, pipH, radius);
        } else {
            this.ctx.rect(pipX, pipY, pipW, pipH);
        }
        this.ctx.clip();

        // Draw webcam video with optional mirror
        if (this.cameraLayout.isMirrored) {
            this.ctx.translate(pipX + pipW, pipY);
            this.ctx.scale(-1, 1);
            this.ctx.drawImage(video, 0, 0, pipW, pipH);
        } else {
            this.ctx.drawImage(video, pipX, pipY, pipW, pipH);
        }

        this.ctx.restore();

        // Draw camera PiP border
        if (preset !== 'Fullscreen') {
            this.ctx.save();
            this.ctx.scale(scaleX, scaleY);
            this.ctx.beginPath();
            if (this.ctx.roundRect) {
                this.ctx.roundRect(pipX, pipY, pipW, pipH, radius);
            } else {
                this.ctx.rect(pipX, pipY, pipW, pipH);
            }
            this.ctx.strokeStyle = '#38BDF8';
            this.ctx.lineWidth = 3;
            this.ctx.stroke();
            this.ctx.restore();
        }
    }

    drawBackground(bg) {
        const w = this.cssWidth, h = this.cssHeight;

        if (bg === 'Blackboard' || bg === 'DarkGrid' || bg === 'DarkRuled') {
            this.ctx.fillStyle = '#1C2127';
            this.ctx.fillRect(0, 0, w, h);
        } else {
            this.ctx.fillStyle = '#FFFFFF';
            this.ctx.fillRect(0, 0, w, h);
        }

        if (bg === 'DarkGrid' || bg === 'LightGrid') {
            this.ctx.strokeStyle = bg === 'DarkGrid' ? '#323A45' : '#E6EBF0';
            this.ctx.lineWidth = 1;
            for (let x = 0; x < w; x += 36) {
                this.ctx.beginPath();
                this.ctx.moveTo(x, 0);
                this.ctx.lineTo(x, h);
                this.ctx.stroke();
            }
            for (let y = 0; y < h; y += 36) {
                this.ctx.beginPath();
                this.ctx.moveTo(0, y);
                this.ctx.lineTo(w, y);
                this.ctx.stroke();
            }
        } else if (bg === 'Ruled' || bg === 'DarkRuled') {
            this.ctx.strokeStyle = bg === 'DarkRuled' ? '#323A45' : '#D2E1F5';
            this.ctx.lineWidth = 1.2;
            for (let y = 60; y < h; y += 32) {
                this.ctx.beginPath();
                this.ctx.moveTo(0, y);
                this.ctx.lineTo(w, y);
                this.ctx.stroke();
            }
        }
    }

    drawStroke(stroke) {
        if (!stroke.points || stroke.points.length === 0) return;

        const c = stroke.color;
        this.ctx.strokeStyle = `rgba(${c.R}, ${c.G}, ${c.B}, ${stroke.opacity})`;
        this.ctx.fillStyle = `rgba(${c.R}, ${c.G}, ${c.B}, ${stroke.opacity})`;
        this.ctx.lineCap = 'round';
        this.ctx.lineJoin = 'round';
        this.ctx.lineWidth = stroke.thickness;

        if (stroke.toolType === 'Text' && stroke.textContent) {
            this.ctx.font = `${stroke.fontSize || 24}px sans-serif`;
            this.ctx.fillText(stroke.textContent, stroke.points[0].x, stroke.points[0].y);
            return;
        }

        if (stroke.points.length === 1) {
            this.ctx.beginPath();
            this.ctx.arc(stroke.points[0].x, stroke.points[0].y, stroke.thickness / 2, 0, Math.PI * 2);
            this.ctx.fill();
            return;
        }

        this.ctx.beginPath();
        this.ctx.moveTo(stroke.points[0].x, stroke.points[0].y);

        for (let i = 1; i < stroke.points.length; i++) {
            const p = stroke.points[i];
            this.ctx.lineTo(p.x, p.y);
        }
        this.ctx.stroke();
    }

    drawShapePreview(p1, p2) {
        const points = this.generateShapePoints(this.activeTool, p1, p2, '00:00:00');
        if (points.length === 0) return;

        const scaleX = this.cssWidth / 1920;
        const scaleY = this.cssHeight / 1080;

        this.ctx.save();
        this.ctx.scale(scaleX, scaleY);
        this.ctx.strokeStyle = this.activeColor;
        this.ctx.lineWidth = this.activeThickness;
        this.ctx.lineCap = 'round';
        this.ctx.lineJoin = 'round';

        this.ctx.beginPath();
        this.ctx.moveTo(points[0].x, points[0].y);
        for (let i = 1; i < points.length; i++) {
            this.ctx.lineTo(points[i].x, points[i].y);
        }
        this.ctx.stroke();
        this.ctx.restore();
    }

    // ==========================================
    // Tool & Palette Controls
    // ==========================================
    bindToolEvents() {
        document.querySelectorAll('.tool-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.tool-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.activeTool = btn.dataset.tool;
            });
        });

        document.querySelectorAll('.color-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.color-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.activeColor = btn.dataset.color;
            });
        });

        const slider = document.getElementById('thicknessSlider');
        const thicknessVal = document.getElementById('thicknessValue');
        slider.addEventListener('input', (e) => {
            this.activeThickness = parseInt(e.target.value, 10);
            thicknessVal.textContent = this.activeThickness;
        });

        const bgSelector = document.getElementById('bgSelector');
        bgSelector.addEventListener('change', (e) => {
            this.activeBackground = e.target.value;
            this.currentPage.background = this.activeBackground;
            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'BackgroundChanged',
                    timestamp: this.getElapsedSessionTime(),
                    pageIndex: this.currentPageIndex,
                    newBackground: this.activeBackground
                });
            }
            this.renderCanvas();
        });

        // Pages
        document.getElementById('btnAddPage').addEventListener('click', () => {
            const newIndex = this.pages.length;
            this.pages.push({
                index: newIndex,
                title: `Page ${newIndex + 1}`,
                background: this.activeBackground,
                strokes: []
            });
            this.setPageIndex(newIndex);
        });

        document.getElementById('btnDeletePage').addEventListener('click', () => {
            if (this.pages.length > 1) {
                this.pages.splice(this.currentPageIndex, 1);
                this.pages.forEach((p, idx) => { p.index = idx; p.title = `Page ${idx + 1}`; });
                this.setPageIndex(Math.min(this.currentPageIndex, this.pages.length - 1));
            }
        });

        document.getElementById('btnPrevPage').addEventListener('click', () => {
            if (this.currentPageIndex > 0) this.setPageIndex(this.currentPageIndex - 1);
        });

        document.getElementById('btnNextPage').addEventListener('click', () => {
            if (this.currentPageIndex < this.pages.length - 1) this.setPageIndex(this.currentPageIndex + 1);
        });

        // Undo / Redo / Clear
        document.getElementById('btnUndo').addEventListener('click', () => this.undo());
        document.getElementById('btnRedo').addEventListener('click', () => this.redo());
        document.getElementById('btnClearCanvas').addEventListener('click', () => {
            if (this.currentPage.strokes.length === 0) return;
            this.undoStack.push({ type: 'clearPage', strokes: [...this.currentPage.strokes], pageIndex: this.currentPageIndex });
            this.currentPage.strokes = [];
            this.renderCanvas();
        });

        window.addEventListener('keydown', (e) => {
            if ((e.metaKey || e.ctrlKey) && e.key === 'z') {
                e.preventDefault();
                if (e.shiftKey) this.redo();
                else this.undo();
            } else if ((e.metaKey || e.ctrlKey) && e.key === 'y') {
                e.preventDefault();
                this.redo();
            }
        });
    }

    setPageIndex(idx) {
        if (idx === this.currentPageIndex) return;
        const prevIdx = this.currentPageIndex;
        this.currentPageIndex = idx;
        document.getElementById('pageIndicator').textContent = `Page ${idx + 1} of ${this.pages.length}`;
        document.getElementById('bgSelector').value = this.currentPage.background;

        if (this.recordingState === 'Recording') {
            this.recordTimelineEvent({
                $eventType: 'PageChanged',
                timestamp: this.getElapsedSessionTime(),
                previousPageIndex: prevIdx,
                newPageIndex: idx
            });
        }

        this.renderCanvas();
    }

    undo() {
        if (this.undoStack.length === 0) return;
        const action = this.undoStack.pop();
        if (action.type === 'addStroke') {
            const page = this.pages[action.pageIndex];
            page.strokes = page.strokes.filter(s => s.id !== action.stroke.id);
            this.redoStack.push(action);
        } else if (action.type === 'eraseStrokes') {
            const page = this.pages[action.pageIndex];
            page.strokes.push(...action.strokes);
            this.redoStack.push(action);
        } else if (action.type === 'clearPage') {
            const page = this.pages[action.pageIndex];
            page.strokes = action.strokes;
            this.redoStack.push(action);
        }
        this.renderCanvas();
    }

    redo() {
        if (this.redoStack.length === 0) return;
        const action = this.redoStack.pop();
        if (action.type === 'addStroke') {
            const page = this.pages[action.pageIndex];
            page.strokes.push(action.stroke);
            this.undoStack.push(action);
        } else if (action.type === 'eraseStrokes') {
            const page = this.pages[action.pageIndex];
            const ids = new Set(action.strokes.map(s => s.id));
            page.strokes = page.strokes.filter(s => !ids.has(s.id));
            this.undoStack.push(action);
        } else if (action.type === 'clearPage') {
            const page = this.pages[action.pageIndex];
            page.strokes = [];
            this.undoStack.push(action);
        }
        this.renderCanvas();
    }

    // ==========================================
    // Presenter Reference Materials (Multi-File & Any Type)
    // ==========================================
    bindSlideEvents() {
        const fileInput = document.getElementById('slideFileInput');
        const btnLoad = document.getElementById('btnLoadSlides');
        const btnClearAll = document.getElementById('btnClearAllSlides');
        const btnRemoveCurrent = document.getElementById('btnRemoveCurrentSlide');
        const sidebarLeft = document.getElementById('sidebarLeft');
        const viewport = document.getElementById('slideViewport');

        btnLoad.addEventListener('click', () => fileInput.click());

        fileInput.addEventListener('change', async (e) => {
            const files = Array.from(e.target.files);
            if (files.length === 0) return;
            await this.addReferenceFiles(files);
            fileInput.value = ''; // Reset input to allow re-uploading same file name if needed
        });

        if (btnClearAll) {
            btnClearAll.addEventListener('click', () => {
                if (confirm('Remove all reference materials?')) {
                    this.clearAllSlides();
                }
            });
        }

        if (btnRemoveCurrent) {
            btnRemoveCurrent.addEventListener('click', () => {
                if (this.currentSlideIndex >= 0 && this.slides.length > 0) {
                    this.removeSlide(this.currentSlideIndex);
                }
            });
        }

        document.getElementById('btnPrevSlide').addEventListener('click', () => {
            if (this.currentSlideIndex > 0) {
                this.currentSlideIndex--;
                this.updateSlideView();
            }
        });

        document.getElementById('btnNextSlide').addEventListener('click', () => {
            if (this.currentSlideIndex < this.slides.length - 1) {
                this.currentSlideIndex++;
                this.updateSlideView();
            }
        });

        // Drag & Drop Reference Material Upload
        const handleDragOver = (e) => {
            e.preventDefault();
            e.stopPropagation();
            viewport.classList.add('drag-over');
        };

        const handleDragLeave = (e) => {
            e.preventDefault();
            e.stopPropagation();
            viewport.classList.remove('drag-over');
        };

        const handleDrop = async (e) => {
            e.preventDefault();
            e.stopPropagation();
            viewport.classList.remove('drag-over');
            if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                await this.addReferenceFiles(Array.from(e.dataTransfer.files));
            }
        };

        viewport.addEventListener('dragover', handleDragOver);
        viewport.addEventListener('dragleave', handleDragLeave);
        viewport.addEventListener('drop', handleDrop);
    }

    async addReferenceFiles(files) {
        for (const file of files) {
            const ext = file.name.split('.').pop().toLowerCase();
            let fileType = 'other';
            let contentUrl = null;
            let textContent = null;

            if (file.type.startsWith('image/') || ['png', 'jpg', 'jpeg', 'gif', 'svg', 'webp', 'bmp'].includes(ext)) {
                fileType = 'image';
                contentUrl = URL.createObjectURL(file);
            } else if (file.type === 'application/pdf' || ext === 'pdf') {
                fileType = 'pdf';
                contentUrl = URL.createObjectURL(file);
            } else if (file.type.startsWith('text/') || ['txt', 'md', 'cs', 'js', 'ts', 'py', 'json', 'html', 'css', 'cpp', 'c', 'h', 'java', 'sql', 'sh', 'xml', 'yaml', 'yml', 'rs', 'go'].includes(ext)) {
                fileType = 'text';
                textContent = await new Promise((resolve) => {
                    const reader = new FileReader();
                    reader.onload = () => resolve(reader.result);
                    reader.onerror = () => resolve('Error reading file content');
                    reader.readAsText(file);
                });
            } else {
                fileType = 'other';
                contentUrl = URL.createObjectURL(file);
            }

            this.slides.push({
                id: this.generateGuid(),
                name: file.name,
                type: fileType,
                url: contentUrl,
                textContent: textContent,
                sizeBytes: file.size
            });
        }

        if (this.currentSlideIndex < 0 || this.currentSlideIndex >= this.slides.length) {
            this.currentSlideIndex = this.slides.length - 1;
        }

        this.updateSlideView();
    }

    removeSlide(index) {
        if (index >= 0 && index < this.slides.length) {
            const item = this.slides[index];
            if (item.url) URL.revokeObjectURL(item.url);
            this.slides.splice(index, 1);
            if (this.currentSlideIndex >= this.slides.length) {
                this.currentSlideIndex = this.slides.length - 1;
            }
            this.updateSlideView();
        }
    }

    clearAllSlides() {
        this.slides.forEach(s => {
            if (s.url) URL.revokeObjectURL(s.url);
        });
        this.slides = [];
        this.currentSlideIndex = -1;
        this.updateSlideView();
    }

    renderSlideTabs() {
        const tabsBar = document.getElementById('slideTabsBar');
        if (!tabsBar) return;

        if (this.slides.length <= 1) {
            tabsBar.style.display = 'none';
            tabsBar.innerHTML = '';
            return;
        }

        tabsBar.style.display = 'flex';
        tabsBar.innerHTML = '';

        this.slides.forEach((item, idx) => {
            const pill = document.createElement('div');
            pill.className = `slide-tab-pill ${idx === this.currentSlideIndex ? 'active' : ''}`;
            
            const icon = item.type === 'image' ? '🖼' : (item.type === 'pdf' ? '📄' : (item.type === 'text' ? '📝' : '📁'));
            pill.innerHTML = `
                <span>${icon} ${item.name}</span>
                <span class="slide-tab-remove" title="Remove file">&times;</span>
            `;

            pill.addEventListener('click', (e) => {
                if (e.target.classList.contains('slide-tab-remove')) {
                    e.stopPropagation();
                    this.removeSlide(idx);
                } else {
                    this.currentSlideIndex = idx;
                    this.updateSlideView();
                }
            });

            tabsBar.appendChild(pill);
        });
    }

    updateSlideView() {
        const img = document.getElementById('currentSlideImg');
        const pdfFrame = document.getElementById('currentSlidePdf');
        const textPre = document.getElementById('currentSlideText');
        const placeholder = document.getElementById('slidePlaceholder');
        const navBar = document.getElementById('slideNavBar');
        const counter = document.getElementById('slideCounter');
        const fileName = document.getElementById('slideFileName');
        const btnClear = document.getElementById('btnClearAllSlides');

        this.renderSlideTabs();

        if (this.slides.length > 0 && this.currentSlideIndex >= 0) {
            const item = this.slides[this.currentSlideIndex];

            placeholder.style.display = 'none';
            navBar.style.display = 'flex';
            if (btnClear) btnClear.style.display = 'inline-flex';

            counter.textContent = `${this.currentSlideIndex + 1} / ${this.slides.length}`;
            if (fileName) fileName.textContent = item.name;

            // Reset all view elements
            img.style.display = 'none';
            pdfFrame.style.display = 'none';
            textPre.style.display = 'none';

            if (item.type === 'image') {
                img.src = item.url;
                img.style.display = 'block';
            } else if (item.type === 'pdf') {
                pdfFrame.src = item.url;
                pdfFrame.style.display = 'block';
            } else if (item.type === 'text') {
                textPre.textContent = item.textContent || '';
                textPre.style.display = 'block';
            } else {
                textPre.textContent = `📁 File: ${item.name}\nSize: ${(item.sizeBytes / 1024).toFixed(1)} KB\n\nPreview not directly renderable. Click load to view.`;
                textPre.style.display = 'block';
            }
        } else {
            img.style.display = 'none';
            pdfFrame.style.display = 'none';
            textPre.style.display = 'none';
            placeholder.style.display = 'block';
            navBar.style.display = 'none';
            if (btnClear) btnClear.style.display = 'none';
        }
    }

    // ==========================================
    // 📱 Mobile Drawer Events (Responsive UI)
    // ==========================================
    bindMobileDrawerEvents() {
        const btnToggleRef = document.getElementById('btnToggleRefSidebar');
        const btnToggleTool = document.getElementById('btnToggleToolSidebar');
        const btnCloseRef = document.getElementById('btnCloseRefSidebar');
        const btnCloseTool = document.getElementById('btnCloseToolSidebar');
        const sidebarLeft = document.getElementById('sidebarLeft');
        const sidebarRight = document.getElementById('sidebarRight');
        const backdrop = document.getElementById('sidebarBackdrop');

        const closeAllDrawers = () => {
            if (sidebarLeft) sidebarLeft.classList.remove('sidebar-drawer-open');
            if (sidebarRight) sidebarRight.classList.remove('sidebar-drawer-open');
            if (backdrop) backdrop.style.display = 'none';
        };

        if (btnToggleRef && sidebarLeft) {
            btnToggleRef.addEventListener('click', () => {
                const isOpen = sidebarLeft.classList.contains('sidebar-drawer-open');
                closeAllDrawers();
                if (!isOpen) {
                    sidebarLeft.classList.add('sidebar-drawer-open');
                    if (backdrop) backdrop.style.display = 'block';
                }
            });
        }

        if (btnToggleTool && sidebarRight) {
            btnToggleTool.addEventListener('click', () => {
                const isOpen = sidebarRight.classList.contains('sidebar-drawer-open');
                closeAllDrawers();
                if (!isOpen) {
                    sidebarRight.classList.add('sidebar-drawer-open');
                    if (backdrop) backdrop.style.display = 'block';
                }
            });
        }

        if (btnCloseRef) btnCloseRef.addEventListener('click', closeAllDrawers);
        if (btnCloseTool) btnCloseTool.addEventListener('click', closeAllDrawers);
        if (backdrop) backdrop.addEventListener('click', closeAllDrawers);
    }

    // ==========================================
    // ↔ Draggable Splitter Resizer
    // ==========================================
    bindResizerEvents() {
        const resizer = document.getElementById('resizerLeft');
        const sidebar = document.getElementById('sidebarLeft');
        const studioMain = document.querySelector('.studio-main');
        if (!resizer || !sidebar || !studioMain) return;

        let isResizing = false;

        const onPointerDown = (e) => {
            isResizing = true;
            resizer.classList.add('resizing');
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            resizer.setPointerCapture(e.pointerId);
        };

        const onPointerMove = (e) => {
            if (!isResizing) return;
            const containerRect = studioMain.getBoundingClientRect();
            let newWidth = e.clientX - containerRect.left;
            
            // Constrain between min 220px and max (containerWidth - 320px)
            const minWidth = 220;
            const maxWidth = Math.max(300, containerRect.width - 320);
            newWidth = Math.max(minWidth, Math.min(newWidth, maxWidth));

            sidebar.style.width = `${newWidth}px`;
            document.documentElement.style.setProperty('--sidebar-left-width', `${newWidth}px`);
            this.setupCanvasSize();
        };

        const onPointerUp = (e) => {
            if (isResizing) {
                isResizing = false;
                resizer.classList.remove('resizing');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                try { resizer.releasePointerCapture(e.pointerId); } catch {}
                this.setupCanvasSize();
            }
        };

        resizer.addEventListener('pointerdown', onPointerDown);
        resizer.addEventListener('pointermove', onPointerMove);
        resizer.addEventListener('pointerup', onPointerUp);
        resizer.addEventListener('pointercancel', onPointerUp);

        // Double-click to toggle 50% split / default 360px
        resizer.addEventListener('dblclick', () => {
            const currentW = sidebar.getBoundingClientRect().width;
            const containerW = studioMain.getBoundingClientRect().width;
            let targetW = 360;
            if (currentW < containerW * 0.45) {
                targetW = Math.round(containerW * 0.5); // expand to 50% split
            } else {
                targetW = 360; // collapse to default
            }
            sidebar.style.width = `${targetW}px`;
            document.documentElement.style.setProperty('--sidebar-left-width', `${targetW}px`);
            this.setupCanvasSize();
        });
    }

    // ==========================================
    // Hardware Audio & Webcam PiP Controls
    // ==========================================
    bindMediaEvents() {
        const btnAudio = document.getElementById('btnEnableAudio');
        const btnCamera = document.getElementById('btnToggleCamera');
        const video = document.getElementById('webcamVideo');
        const camPlaceholder = document.getElementById('camPlaceholder');
        const cameraPip = document.getElementById('cameraPip');
        const layoutSelector = document.getElementById('cameraLayoutSelector');
        const chkMirror = document.getElementById('chkMirror');

        btnAudio.addEventListener('click', async () => {
            await this.enableMicrophone();
        });

        btnCamera.addEventListener('click', async () => {
            if (this.cameraStream) {
                this.cameraStream.getTracks().forEach(t => t.stop());
                this.cameraStream = null;
                video.srcObject = null;
                camPlaceholder.style.display = 'flex';
                btnCamera.textContent = 'Enable Camera';
                btnCamera.classList.remove('btn-primary');
                btnCamera.classList.add('btn-secondary');
            } else {
                await this.enableCamera();
            }
        });

        layoutSelector.addEventListener('change', (e) => {
            const preset = e.target.value;
            cameraPip.className = `camera-pip pip-${preset.toLowerCase().replace('-', '')}`;
            if (preset === 'BottomRight') cameraPip.className = 'camera-pip pip-bottom-right';
            if (preset === 'BottomLeft') cameraPip.className = 'camera-pip pip-bottom-left';
            if (preset === 'TopRight') cameraPip.className = 'camera-pip pip-top-right';
            if (preset === 'TopLeft') cameraPip.className = 'camera-pip pip-top-left';
            if (preset === 'Fullscreen') cameraPip.className = 'camera-pip pip-fullscreen';
            if (preset === 'Hidden') cameraPip.className = 'camera-pip pip-hidden';

            this.cameraLayout.preset = preset;
            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'CameraLayoutChanged',
                    timestamp: this.getElapsedSessionTime(),
                    layout: { preset: preset, isMirrored: this.cameraLayout.isMirrored, isVisible: preset !== 'Hidden' }
                });
            }
            this.renderCanvas();
        });

        chkMirror.addEventListener('change', (e) => {
            this.cameraLayout.isMirrored = e.target.checked;
            cameraPip.classList.toggle('no-mirror', !e.target.checked);
            this.renderCanvas();
        });
    }

    async enableMicrophone() {
        const btnAudio = document.getElementById('btnEnableAudio');
        const audioStatus = document.getElementById('audioStatus');

        try {
            this.audioStream = await navigator.mediaDevices.getUserMedia({ audio: true });
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
            const source = this.audioContext.createMediaStreamSource(this.audioStream);
            this.analyserNode = this.audioContext.createAnalyser();
            this.analyserNode.fftSize = 256;
            source.connect(this.analyserNode);

            btnAudio.textContent = '✓ Mic Connected';
            btnAudio.classList.remove('btn-secondary');
            btnAudio.classList.add('btn-primary');
            audioStatus.textContent = 'Microphone active — Live VU metering enabled';

            this.startVuMeterLoop();
            return true;
        } catch (err) {
            audioStatus.textContent = `Microphone error: ${err.message}`;
            return false;
        }
    }

    async enableCamera() {
        const btnCamera = document.getElementById('btnToggleCamera');
        const video = document.getElementById('webcamVideo');
        const camPlaceholder = document.getElementById('camPlaceholder');

        try {
            this.cameraStream = await navigator.mediaDevices.getUserMedia({
                video: { width: { ideal: 1280 }, height: { ideal: 720 } }
            });
            video.srcObject = this.cameraStream;
            await video.play();
            camPlaceholder.style.display = 'none';
            btnCamera.textContent = '✓ Camera Active';
            btnCamera.classList.remove('btn-secondary');
            btnCamera.classList.add('btn-primary');
            this.renderCanvas();
            return true;
        } catch (err) {
            alert(`Camera access error: ${err.message}`);
            return false;
        }
    }

    startVuMeterLoop() {
        const vuBar = document.getElementById('vuMeterBar');
        const dataArray = new Uint8Array(this.analyserNode.frequencyBinCount);

        const updateMeter = () => {
            if (this.analyserNode) {
                this.analyserNode.getByteFrequencyData(dataArray);
                let sum = 0;
                for (let i = 0; i < dataArray.length; i++) sum += dataArray[i];
                const avg = sum / dataArray.length;
                const percent = Math.min(100, Math.round((avg / 128) * 100));
                vuBar.style.width = `${percent}%`;
            }
            requestAnimationFrame(updateMeter);
        };
        updateMeter();
    }

    getBestMimeType() {
        const types = [
            'video/mp4;codecs=avc1.42E01E,mp4a.40.2',
            'video/mp4;codecs=avc1',
            'video/mp4',
            'video/webm;codecs=h264',
            'video/webm;codecs=vp9,opus',
            'video/webm;codecs=vp8,opus',
            'video/webm'
        ];
        for (const t of types) {
            if (MediaRecorder.isTypeSupported && MediaRecorder.isTypeSupported(t)) {
                return t;
            }
        }
        return '';
    }

    // ==========================================
    // Synchronized Recording Engine & Local Storage
    // ==========================================
    bindRecordingEvents() {
        const btnRecord = document.getElementById('btnRecord');
        const btnPause = document.getElementById('btnPause');
        const btnResume = document.getElementById('btnResume');
        const btnStop = document.getElementById('btnStop');

        btnRecord.addEventListener('click', () => this.startRecording());
        btnPause.addEventListener('click', () => this.pauseRecording());
        btnResume.addEventListener('click', () => this.resumeRecording());
        btnStop.addEventListener('click', () => this.stopRecording());
    }

    async startRecording() {
        if (!this.audioStream) {
            await this.enableMicrophone();
        }

        this.recordingState = 'Recording';
        this.sessionStartTime = Date.now();
        this.totalPausedDuration = 0;
        this.timelineEvents = [];

        // 1. Microphone Audio Stream Recorder
        this.audioChunks = [];
        if (this.audioStream) {
            try {
                this.audioRecorder = new MediaRecorder(this.audioStream);
                this.audioRecorder.ondataavailable = (e) => {
                    if (e.data && e.data.size > 0) this.audioChunks.push(e.data);
                };
                this.audioRecorder.start(250);
            } catch (err) {
                console.warn('Audio recorder start warning:', err);
            }
        }

        // 2. Camera Video Stream Recorder
        this.cameraChunks = [];
        if (this.cameraStream && this.cameraLayout.preset !== 'Hidden') {
            try {
                const camMime = this.getBestMimeType();
                this.cameraRecorder = new MediaRecorder(this.cameraStream, camMime ? { mimeType: camMime } : undefined);
                this.cameraRecorder.ondataavailable = (e) => {
                    if (e.data && e.data.size > 0) this.cameraChunks.push(e.data);
                };
                this.cameraRecorder.start(250);
            } catch (err) {
                console.warn('Camera recorder start warning:', err);
            }
        }

        // 3. Combined Canvas + Webcam + Mic Audio Stream Local Recorder (Direct Browser MP4 Video Generation)
        try {
            const canvasStream = this.canvas.captureStream(30);
            const combinedTracks = [...canvasStream.getVideoTracks()];
            if (this.audioStream && this.audioStream.getAudioTracks().length > 0) {
                combinedTracks.push(this.audioStream.getAudioTracks()[0]);
            }
            const localStream = new MediaStream(combinedTracks);
            this.canvasChunks = [];
            const bestMime = this.getBestMimeType();
            const recorderOpts = bestMime ? { mimeType: bestMime, videoBitsPerSecond: 4000000 } : undefined;
            
            this.canvasRecorder = new MediaRecorder(localStream, recorderOpts);
            this.canvasRecorder.ondataavailable = (e) => {
                if (e.data && e.data.size > 0) this.canvasChunks.push(e.data);
            };
            this.canvasRecorder.start(250);
        } catch (err) {
            console.warn('Local canvas recorder warning:', err);
        }

        this.updateRecordingUi();
        this.startTimer();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: '00:00:00',
            oldState: 'Stopped',
            newState: 'Recording'
        });

        this.recordTimelineEvent({
            $eventType: 'CameraLayoutChanged',
            timestamp: '00:00:00',
            layout: { preset: this.cameraLayout.preset, isMirrored: this.cameraLayout.isMirrored, isVisible: true }
        });
    }

    pauseRecording() {
        this.recordingState = 'Paused';
        this.pauseStartTime = Date.now();
        
        if (this.audioRecorder && this.audioRecorder.state === 'recording') this.audioRecorder.pause();
        if (this.cameraRecorder && this.cameraRecorder.state === 'recording') this.cameraRecorder.pause();
        if (this.canvasRecorder && this.canvasRecorder.state === 'recording') this.canvasRecorder.pause();

        this.updateRecordingUi();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: this.getElapsedSessionTime(),
            oldState: 'Recording',
            newState: 'Paused'
        });
    }

    resumeRecording() {
        if (this.pauseStartTime) {
            this.totalPausedDuration += Date.now() - this.pauseStartTime;
            this.pauseStartTime = null;
        }
        this.recordingState = 'Recording';

        if (this.audioRecorder && this.audioRecorder.state === 'paused') this.audioRecorder.resume();
        if (this.cameraRecorder && this.cameraRecorder.state === 'paused') this.cameraRecorder.resume();
        if (this.canvasRecorder && this.canvasRecorder.state === 'paused') this.canvasRecorder.resume();

        this.updateRecordingUi();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: this.getElapsedSessionTime(),
            oldState: 'Paused',
            newState: 'Recording'
        });
    }

    stopRecording() {
        const finalTime = this.getElapsedSessionTime();
        this.recordingState = 'Stopped';
        clearInterval(this.timerInterval);

        // Stop Audio Recorder
        if (this.audioRecorder && this.audioRecorder.state !== 'inactive') {
            this.audioRecorder.onstop = () => {
                this.recordedAudioBlob = new Blob(this.audioChunks, { type: 'audio/webm' });
            };
            this.audioRecorder.stop();
        }

        // Stop Camera Recorder
        if (this.cameraRecorder && this.cameraRecorder.state !== 'inactive') {
            this.cameraRecorder.onstop = () => {
                this.recordedCameraBlob = new Blob(this.cameraChunks, { type: 'video/webm' });
            };
            this.cameraRecorder.stop();
        }

        // Stop Local Canvas Recorder & Save immediately to IndexedDB Local Storage as MP4
        if (this.canvasRecorder && this.canvasRecorder.state !== 'inactive') {
            this.canvasRecorder.onstop = async () => {
                const mime = this.getBestMimeType() || 'video/mp4';
                this.localVideoBlob = new Blob(this.canvasChunks, { type: mime.includes('mp4') ? 'video/mp4' : 'video/webm' });
                
                // Create thumbnail snapshot
                const thumb = this.canvas.toDataURL('image/jpeg', 0.6);
                const recordingEntry = {
                    id: this.generateGuid(),
                    title: `Lesson ${new Date().toLocaleDateString()} ${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`,
                    createdAt: new Date().toISOString(),
                    duration: finalTime.split('.')[0],
                    format: 'MP4 (Synchronized Studio Video)',
                    sizeBytes: this.localVideoBlob.size,
                    blob: this.localVideoBlob,
                    thumbnail: thumb,
                    pages: JSON.parse(JSON.stringify(this.pages)),
                    events: JSON.parse(JSON.stringify(this.timelineEvents))
                };

                await this.storage.saveRecording(recordingEntry);
                this.updateLibraryBadge();
            };
            this.canvasRecorder.stop();
        }

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: finalTime,
            oldState: 'Recording',
            newState: 'Stopped'
        });

        this.updateRecordingUi();
        
        // Open export dialog with instant download ready
        const instantBox = document.getElementById('instantDownloadBox');
        if (instantBox) instantBox.style.display = 'block';
        document.getElementById('exportModal').style.display = 'flex';
    }

    getElapsedSessionTime() {
        if (!this.sessionStartTime) return '00:00:00.000';
        let now = Date.now();
        if (this.recordingState === 'Paused' && this.pauseStartTime) {
            now = this.pauseStartTime;
        }
        const totalMs = (now - this.sessionStartTime) - this.totalPausedDuration;
        const totalSec = Math.max(0, totalMs / 1000);
        return this.formatTimeSpan(totalSec);
    }

    formatTimeSpan(seconds) {
        const hrs = Math.floor(seconds / 3600);
        const mins = Math.floor((seconds % 3600) / 60);
        const secs = Math.floor(seconds % 60);
        const ms = Math.floor((seconds % 1) * 1000);
        return `${String(hrs).padStart(2, '0')}:${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}.${String(ms).padStart(3, '0')}`;
    }

    startTimer() {
        clearInterval(this.timerInterval);
        this.timerInterval = setInterval(() => {
            const timeStr = this.getElapsedSessionTime().split('.')[0];
            document.getElementById('timerDisplay').textContent = timeStr;
        }, 100);
    }

    updateRecordingUi() {
        const pill = document.getElementById('recordingPill');
        const statusLabel = document.getElementById('statusLabel');
        const btnRecord = document.getElementById('btnRecord');
        const btnPause = document.getElementById('btnPause');
        const btnResume = document.getElementById('btnResume');
        const btnStop = document.getElementById('btnStop');

        pill.className = `recording-pill state-${this.recordingState.toLowerCase()}`;
        statusLabel.textContent = this.recordingState.toUpperCase();

        if (this.recordingState === 'Recording') {
            btnRecord.style.display = 'none';
            btnPause.style.display = 'inline-flex';
            btnResume.style.display = 'none';
            btnStop.style.display = 'inline-flex';
        } else if (this.recordingState === 'Paused') {
            btnRecord.style.display = 'none';
            btnPause.style.display = 'none';
            btnResume.style.display = 'inline-flex';
            btnStop.style.display = 'inline-flex';
        } else {
            btnRecord.style.display = 'inline-flex';
            btnPause.style.display = 'none';
            btnResume.style.display = 'none';
            btnStop.style.display = 'none';
        }
    }

    recordTimelineEvent(evt) {
        evt.eventId = this.generateGuid();
        evt.wallClockUtc = new Date().toISOString();
        this.timelineEvents.push(evt);
    }

    // ==========================================
    // Video Export & Instant MP4 Download
    // ==========================================
    bindExportEvents() {
        const modal = document.getElementById('exportModal');
        const btnExport = document.getElementById('btnExport');
        const btnClose = document.getElementById('btnCloseModal');
        const btnCancel = document.getElementById('btnCancelExport');
        const btnStart = document.getElementById('btnStartExport');
        const btnInstant = document.getElementById('btnInstantDownload');
        const instantBox = document.getElementById('instantDownloadBox');

        const showModal = () => {
            if (instantBox) {
                instantBox.style.display = this.localVideoBlob ? 'block' : 'none';
            }
            modal.style.display = 'flex';
        };

        btnExport.addEventListener('click', showModal);
        btnClose.addEventListener('click', () => modal.style.display = 'none');
        btnCancel.addEventListener('click', () => modal.style.display = 'none');

        // Instant MP4 Download button
        if (btnInstant) {
            btnInstant.addEventListener('click', () => {
                if (this.localVideoBlob) {
                    const url = window.URL.createObjectURL(this.localVideoBlob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = `WriteStudio_Lesson_${Date.now()}.mp4`;
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                    modal.style.display = 'none';
                } else {
                    alert('No recording found yet. Click ● RECORD to create a video.');
                }
            });
        }

        btnStart.addEventListener('click', async () => {
            btnStart.disabled = true;
            const progressContainer = document.getElementById('exportProgressContainer');
            const progressBar = document.getElementById('exportProgressBar');
            const statusText = document.getElementById('exportStatusText');

            progressContainer.style.display = 'block';
            progressBar.style.width = '20%';
            statusText.textContent = 'Packaging session tracks and rasterizing vector strokes...';

            let progressPct = 20;
            const progressTimer = setInterval(() => {
                if (progressPct < 90) {
                    progressPct += 5;
                    progressBar.style.width = `${progressPct}%`;
                    if (progressPct >= 40 && progressPct < 70) {
                        statusText.textContent = 'FFmpeg encoding video & audio tracks...';
                    } else if (progressPct >= 70) {
                        statusText.textContent = 'Finalizing MP4 container...';
                    }
                }
            }, 300);

            let durationStr = this.getElapsedSessionTime();
            if (durationStr === '00:00:00' || durationStr === '00:00:00.000') {
                durationStr = '00:00:05.000';
            }

            const resVal = document.getElementById('exportResolution').value.split('x');
            const targetW = parseInt(resVal[0], 10) || 1280;
            const targetH = parseInt(resVal[1], 10) || 720;
            const targetFps = parseInt(document.getElementById('exportFps').value, 10) || 30;

            const payload = {
                sessionId: this.generateGuid(),
                metadata: {
                    title: 'Interactive Studio Lesson',
                    author: 'Presenter',
                    canvasWidth: targetW,
                    canvasHeight: targetH,
                    targetFps: targetFps,
                    duration: durationStr,
                    totalPages: this.pages.length,
                    hasAudioTrack: !!this.recordedAudioBlob,
                    hasWebcamTrack: !!this.recordedCameraBlob
                },
                pages: this.pages.map(page => ({
                    id: this.generateGuid(),
                    index: page.index,
                    title: page.title,
                    background: page.background || 'Blackboard',
                    strokes: (page.strokes || []).map(stroke => ({
                        id: stroke.id || this.generateGuid(),
                        pageIndex: stroke.pageIndex || 0,
                        startTime: stroke.startTime || '00:00:00.000',
                        endTime: stroke.endTime || '00:00:01.000',
                        color: stroke.color || { R: 255, G: 255, B: 255, A: 255 },
                        thickness: stroke.thickness || 4,
                        opacity: stroke.opacity || 1.0,
                        toolType: stroke.toolType || 'Pen',
                        textContent: stroke.textContent || null,
                        fontSize: stroke.fontSize || 24,
                        points: (stroke.points || []).map(pt => ({
                            x: pt.x,
                            y: pt.y,
                            pressure: pt.pressure || 0.5,
                            timestamp: pt.timestamp || '00:00:00.000'
                        }))
                    }))
                })),
                events: (this.timelineEvents || []).map(evt => {
                    const clone = { ...evt };
                    if (clone.oldState === 0) clone.oldState = 'Stopped';
                    if (clone.oldState === 1) clone.oldState = 'Recording';
                    if (clone.oldState === 2) clone.oldState = 'Paused';
                    if (clone.newState === 0) clone.newState = 'Stopped';
                    if (clone.newState === 1) clone.newState = 'Recording';
                    if (clone.newState === 2) clone.newState = 'Paused';
                    return clone;
                })
            };

            const formData = new FormData();
            formData.append('session', JSON.stringify(payload));

            if (this.recordedAudioBlob) {
                formData.append('audioFile', this.recordedAudioBlob, 'recording.webm');
            }

            if (this.recordedCameraBlob) {
                formData.append('cameraFile', this.recordedCameraBlob, 'webcam.webm');
            }

            try {
                const response = await fetch('/api/export', {
                    method: 'POST',
                    body: formData
                });

                clearInterval(progressTimer);

                if (!response.ok) {
                    const errorText = await response.text();
                    throw new Error(errorText || `HTTP ${response.status}`);
                }

                progressBar.style.width = '100%';
                statusText.textContent = 'Saving to Local Storage & downloading MP4...';

                const blob = await response.blob();
                
                // Save master MP4 into Local Storage (IndexedDB)
                const thumb = this.canvas.toDataURL('image/jpeg', 0.6);
                const mp4Entry = {
                    id: this.generateGuid(),
                    title: `Master HD MP4 — ${new Date().toLocaleDateString()} ${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`,
                    createdAt: new Date().toISOString(),
                    duration: durationStr.split('.')[0],
                    format: `MP4 (${targetW}x${targetH} @ ${targetFps}fps)`,
                    sizeBytes: blob.size,
                    blob: blob,
                    thumbnail: thumb,
                    pages: JSON.parse(JSON.stringify(this.pages)),
                    events: JSON.parse(JSON.stringify(this.timelineEvents))
                };
                await this.storage.saveRecording(mp4Entry);
                this.updateLibraryBadge();

                // Trigger direct MP4 download
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `WriteStudio_Master_${Date.now()}.mp4`;
                document.body.appendChild(a);
                a.click();
                a.remove();

                setTimeout(() => {
                    modal.style.display = 'none';
                    progressContainer.style.display = 'none';
                    btnStart.disabled = false;
                }, 1200);
            } catch (err) {
                clearInterval(progressTimer);
                btnStart.disabled = false;
                progressContainer.style.display = 'none';

                if (this.localVideoBlob) {
                    // Directly download the complete local MP4 video
                    const url = window.URL.createObjectURL(this.localVideoBlob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = `WriteStudio_Lesson_${Date.now()}.mp4`;
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                    modal.style.display = 'none';
                } else {
                    alert(`Export notice: ${err.message}`);
                }
            }
        });
    }

    // ==========================================
    // 📚 Recordings Library & Local Storage UI
    // ==========================================
    bindLibraryEvents() {
        const btnOpen = document.getElementById('btnOpenLibrary');
        const modal = document.getElementById('libraryModal');
        const btnClose = document.getElementById('btnCloseLibrary');
        const btnCloseFooter = document.getElementById('btnCloseLibraryFooter');
        const btnClearAll = document.getElementById('btnClearAllRecordings');

        const playerModal = document.getElementById('videoPlayerModal');
        const btnClosePlayer = document.getElementById('btnClosePlayer');
        const btnClosePlayerFooter = document.getElementById('btnClosePlayerFooter');

        btnOpen.addEventListener('click', async () => {
            await this.renderLibrary();
            modal.style.display = 'flex';
        });

        btnClose.addEventListener('click', () => modal.style.display = 'none');
        btnCloseFooter.addEventListener('click', () => modal.style.display = 'none');

        btnClearAll.addEventListener('click', async () => {
            if (confirm('Are you sure you want to delete all saved recordings from local storage?')) {
                await this.storage.clearAllRecordings();
                await this.renderLibrary();
                this.updateLibraryBadge();
            }
        });

        const closePlayer = () => {
            const player = document.getElementById('libraryVideoPlayer');
            player.pause();
            player.src = '';
            playerModal.style.display = 'none';
        };

        btnClosePlayer.addEventListener('click', closePlayer);
        btnClosePlayerFooter.addEventListener('click', closePlayer);
    }

    async updateLibraryBadge() {
        try {
            const list = await this.storage.getAllRecordings();
            const badge = document.getElementById('libraryBadge');
            if (badge) badge.textContent = list.length;
        } catch { }
    }

    async renderLibrary() {
        const listContainer = document.getElementById('recordingsList');
        const emptyNotice = document.getElementById('emptyLibraryNotice');
        const infoText = document.getElementById('storageInfoText');

        const items = await this.storage.getAllRecordings();
        this.updateLibraryBadge();

        if (items.length === 0) {
            listContainer.innerHTML = '';
            emptyNotice.style.display = 'block';
            infoText.textContent = 'Stored Locally in Browser: 0 recordings';
            return;
        }

        emptyNotice.style.display = 'none';
        let totalBytes = 0;
        listContainer.innerHTML = '';

        items.forEach(item => {
            totalBytes += item.sizeBytes || 0;
            const sizeMb = ((item.sizeBytes || 0) / (1024 * 1024)).toFixed(1);
            const dateStr = new Date(item.createdAt).toLocaleString();

            const card = document.createElement('div');
            card.className = 'recording-card';
            card.innerHTML = `
                <img class="recording-thumb" src="${item.thumbnail || ''}" alt="Lesson Thumbnail">
                <div class="recording-info">
                    <div class="recording-title">${item.title}</div>
                    <div class="recording-details">
                        <span>⏱ ${item.duration}</span>
                        <span>•</span>
                        <span>💾 ${sizeMb} MB</span>
                        <span>•</span>
                        <span>${item.format || 'MP4 Video'}</span>
                        <span>•</span>
                        <span>📅 ${dateStr}</span>
                    </div>
                </div>
                <div class="recording-actions">
                    <button class="btn btn-sm btn-primary btn-play-rec" data-id="${item.id}">▶ Play</button>
                    <button class="btn btn-sm btn-secondary btn-dl-rec" data-id="${item.id}">⬇ Download MP4</button>
                    <button class="btn btn-sm btn-danger btn-del-rec" data-id="${item.id}">🗑</button>
                </div>
            `;

            // Play Video
            card.querySelector('.btn-play-rec').addEventListener('click', () => this.playStoredVideo(item));

            // Download Video
            card.querySelector('.btn-dl-rec').addEventListener('click', () => {
                const url = URL.createObjectURL(item.blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `${item.title.replace(/[^a-zA-Z0-9_-]/g, '_')}.mp4`;
                document.body.appendChild(a);
                a.click();
                a.remove();
            });

            // Delete Video
            card.querySelector('.btn-del-rec').addEventListener('click', async () => {
                if (confirm(`Delete "${item.title}" from local storage?`)) {
                    await this.storage.deleteRecording(item.id);
                    await this.renderLibrary();
                }
            });

            listContainer.appendChild(card);
        });

        const totalMb = (totalBytes / (1024 * 1024)).toFixed(1);
        infoText.textContent = `Stored Locally in Browser: ${items.length} recordings (${totalMb} MB used)`;
    }

    playStoredVideo(item) {
        const playerModal = document.getElementById('videoPlayerModal');
        const player = document.getElementById('libraryVideoPlayer');
        const title = document.getElementById('playerModalTitle');
        const btnDl = document.getElementById('btnDownloadCurrentVideo');

        const url = URL.createObjectURL(item.blob);
        player.src = url;
        title.textContent = `▶ ${item.title} (${item.duration})`;

        btnDl.href = url;
        btnDl.download = `${item.title.replace(/[^a-zA-Z0-9_-]/g, '_')}.mp4`;

        playerModal.style.display = 'flex';
        player.play();
    }

    // Utilities
    hexToRgba(hex, alpha = 1.0) {
        hex = hex.replace('#', '');
        const r = parseInt(hex.substring(0, 2), 16);
        const g = parseInt(hex.substring(2, 4), 16);
        const b = parseInt(hex.substring(4, 6), 16);
        const a = Math.round(alpha * 255);
        return { R: r, G: g, B: b, A: a };
    }

    capitalize(str) {
        return str.charAt(0).toUpperCase() + str.slice(1);
    }

    generateGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            const r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }
}

// Bootstrap on DOM load
window.addEventListener('DOMContentLoaded', () => {
    window.studio = new WriteStudioEngine();
});
