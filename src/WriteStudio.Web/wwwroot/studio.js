// WriteStudio Interactive Web Studio Client Engine

class WriteStudioEngine {
    constructor() {
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
        this.recordingState = 'Stopped'; // 'Stopped', 'Recording', 'Paused'
        this.sessionStartTime = null;
        this.pauseStartTime = null;
        this.totalPausedDuration = 0;
        this.timelineEvents = [];
        this.timerInterval = null;

        // Presenter Reference Slides
        this.slides = [];
        this.currentSlideIndex = -1;

        // Hardware & Media
        this.audioContext = null;
        this.analyserNode = null;
        this.audioStream = null;
        this.cameraStream = null;
        this.cameraLayout = {
            preset: 'BottomRight',
            isMirrored: true,
            isVisible: true
        };

        this.init();
    }

    init() {
        this.setupCanvasSize();
        window.addEventListener('resize', () => this.setupCanvasSize());

        this.bindCanvasEvents();
        this.bindToolEvents();
        this.bindSlideEvents();
        this.bindMediaEvents();
        this.bindRecordingEvents();
        this.bindExportEvents();

        this.renderCanvas();
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
                    y: cy + ry * Math.Sin(angle),
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
        const initialCount = page.strokes.length;
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
    // Canvas Rendering Loop
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
    // Presenter Reference Slides (Private)
    // ==========================================
    bindSlideEvents() {
        const fileInput = document.getElementById('slideFileInput');
        const btnLoad = document.getElementById('btnLoadSlides');
        const img = document.getElementById('currentSlideImg');
        const placeholder = document.getElementById('slidePlaceholder');
        const navBar = document.getElementById('slideNavBar');
        const counter = document.getElementById('slideCounter');

        btnLoad.addEventListener('click', () => fileInput.click());

        fileInput.addEventListener('change', (e) => {
            const files = Array.from(e.target.files);
            if (files.length === 0) return;

            this.slides = [];
            files.forEach((file, index) => {
                const url = URL.createObjectURL(file);
                this.slides.push({ name: file.name, url: url, pageNumber: index + 1 });
            });

            this.currentSlideIndex = 0;
            this.updateSlideView();
        });

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
    }

    updateSlideView() {
        const img = document.getElementById('currentSlideImg');
        const placeholder = document.getElementById('slidePlaceholder');
        const navBar = document.getElementById('slideNavBar');
        const counter = document.getElementById('slideCounter');

        if (this.slides.length > 0 && this.currentSlideIndex >= 0) {
            img.src = this.slides[this.currentSlideIndex].url;
            img.style.display = 'block';
            placeholder.style.display = 'none';
            navBar.style.display = 'flex';
            counter.textContent = `${this.currentSlideIndex + 1} / ${this.slides.length}`;
        } else {
            img.style.display = 'none';
            placeholder.style.display = 'block';
            navBar.style.display = 'none';
        }
    }

    // ==========================================
    // Hardware Audio & Webcam PiP Controls
    // ==========================================
    bindMediaEvents() {
        // Audio
        const btnAudio = document.getElementById('btnEnableAudio');
        const audioStatus = document.getElementById('audioStatus');
        const vuBar = document.getElementById('vuMeterBar');

        btnAudio.addEventListener('click', async () => {
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
            } catch (err) {
                audioStatus.textContent = `Microphone access error: ${err.message}`;
            }
        });

        // Camera
        const btnCamera = document.getElementById('btnToggleCamera');
        const video = document.getElementById('webcamVideo');
        const camPlaceholder = document.getElementById('camPlaceholder');
        const cameraPip = document.getElementById('cameraPip');
        const layoutSelector = document.getElementById('cameraLayoutSelector');
        const chkMirror = document.getElementById('chkMirror');

        btnCamera.addEventListener('click', async () => {
            if (this.cameraStream) {
                this.cameraStream.getTracks().forEach(t => t.stop());
                this.cameraStream = null;
                video.srcObject = null;
                camPlaceholder.style.display = 'flex';
                btnCamera.textContent = 'Enable Camera';
            } else {
                try {
                    this.cameraStream = await navigator.mediaDevices.getUserMedia({ video: true });
                    video.srcObject = this.cameraStream;
                    camPlaceholder.style.display = 'none';
                    btnCamera.textContent = 'Disable Camera';
                } catch (err) {
                    alert(`Camera access error: ${err.message}`);
                }
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
        });

        chkMirror.addEventListener('change', (e) => {
            this.cameraLayout.isMirrored = e.target.checked;
            cameraPip.classList.toggle('no-mirror', !e.target.checked);
        });
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

    // ==========================================
    // Synchronized Recording Engine
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

    startRecording() {
        this.recordingState = 'Recording';
        this.sessionStartTime = Date.now();
        this.totalPausedDuration = 0;
        this.timelineEvents = [];

        this.updateRecordingUi();
        this.startTimer();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: '00:00:00',
            oldState: 0,
            newState: 1
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
        this.updateRecordingUi();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: this.getElapsedSessionTime(),
            oldState: 1,
            newState: 2
        });
    }

    resumeRecording() {
        if (this.pauseStartTime) {
            this.totalPausedDuration += Date.now() - this.pauseStartTime;
            this.pauseStartTime = null;
        }
        this.recordingState = 'Recording';
        this.updateRecordingUi();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: this.getElapsedSessionTime(),
            oldState: 2,
            newState: 1
        });
    }

    stopRecording() {
        const finalTime = this.getElapsedSessionTime();
        this.recordingState = 'Stopped';
        clearInterval(this.timerInterval);

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: finalTime,
            oldState: 1,
            newState: 0
        });

        this.updateRecordingUi();
        document.getElementById('exportModal').style.display = 'flex';
    }

    getElapsedSessionTime() {
        if (!this.sessionStartTime) return '00:00:00';
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
    // Video Export via .NET 10 Skia & FFmpeg
    // ==========================================
    bindExportEvents() {
        const modal = document.getElementById('exportModal');
        const btnExport = document.getElementById('btnExport');
        const btnClose = document.getElementById('btnCloseModal');
        const btnCancel = document.getElementById('btnCancelExport');
        const btnStart = document.getElementById('btnStartExport');

        btnExport.addEventListener('click', () => modal.style.display = 'flex');
        btnClose.addEventListener('click', () => modal.style.display = 'none');
        btnCancel.addEventListener('click', () => modal.style.display = 'none');

        btnStart.addEventListener('click', async () => {
            btnStart.disabled = true;
            const progressContainer = document.getElementById('exportProgressContainer');
            const progressBar = document.getElementById('exportProgressBar');
            const statusText = document.getElementById('exportStatusText');

            progressContainer.style.display = 'block';
            progressBar.style.width = '30%';
            statusText.textContent = 'Packaging timeline and rasterizing vector strokes...';

            const payload = {
                sessionId: this.generateGuid(),
                metadata: {
                    title: 'Interactive Studio Lesson',
                    author: 'Presenter',
                    canvasWidth: 1920,
                    canvasHeight: 1080,
                    targetFps: 30,
                    duration: this.getElapsedSessionTime(),
                    totalPages: this.pages.length
                },
                pages: this.pages,
                events: this.timelineEvents
            };

            try {
                progressBar.style.width = '60%';
                statusText.textContent = 'FFmpeg encoding (1080p @ 30 FPS)...';

                const response = await fetch('/api/export', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });

                if (!response.ok) throw new Error(await response.text());

                progressBar.style.width = '100%';
                statusText.textContent = 'Download started!';

                const blob = await response.blob();
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `WriteStudio_Lesson_${Date.now()}.mp4`;
                document.body.appendChild(a);
                a.click();
                a.remove();

                setTimeout(() => {
                    modal.style.display = 'none';
                    progressContainer.style.display = 'none';
                    btnStart.disabled = false;
                }, 1500);
            } catch (err) {
                alert(`Export error: ${err.message}`);
                btnStart.disabled = false;
                progressContainer.style.display = 'none';
            }
        });
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
