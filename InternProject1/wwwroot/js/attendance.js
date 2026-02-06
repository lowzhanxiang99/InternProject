// ========== GLOBAL VARIABLES ==========
let workingTimer = null;
let breakTimer = null;
let workingSeconds = 0;
let breakSeconds = 0;
let breakStartTime = null;
let isOnBreak = false; // Will be initialized from Razor
let currentSort = {
    column: 'date',
    order: 'desc',
    currentPage: 1,
    totalPages: 1
};
let cachedElements = {};
let clockElements = {};
let lastSecond = -1;

// ========== INITIALIZATION ==========
function initializeAttendancePage(config) {
    isOnBreak = config.isOnBreak;

    initializeClockFace();
    updateAnalogClock();
    setInterval(updateAnalogClock, 50);

    // Live clock update for check-in time
    const updateTime = debounce(function () {
        const now = new Date();
        const timeString = now.toLocaleTimeString('en-US', {
            hour12: true,
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
        const checkInTimeElement = getElement('checkInTime');
        if (checkInTimeElement) {
            checkInTimeElement.textContent = timeString;
        }
    }, 100);

    setInterval(updateTime, 1000);
    updateTime();
    initializeTimeDisplaysFromConfig(config);

    // Initialize calendar navigation buttons
    const prevMonthBtn = getElement('prevMonth');
    const nextMonthBtn = getElement('nextMonth');

    if (prevMonthBtn) {
        prevMonthBtn.addEventListener('click', function () {
            let newMonth = config.selectedMonth - 1;
            let newYear = config.selectedYear;

            if (newMonth < 1) {
                newMonth = 12;
                newYear--;
            }

            window.location.href = `?month=${newMonth}&year=${newYear}`;
        });
    }

    if (nextMonthBtn) {
        nextMonthBtn.addEventListener('click', function () {
            let newMonth = config.selectedMonth + 1;
            let newYear = config.selectedYear;

            if (newMonth > 12) {
                newMonth = 1;
                newYear++;
            }

            window.location.href = `?month=${newMonth}&year=${newYear}`;
        });
    }

    // Month/Year Modal Handler
    const calendarMonthYear = getElement('calendarMonthYear');
    if (calendarMonthYear) {
        calendarMonthYear.addEventListener('click', function () {
            const monthYearModal = new bootstrap.Modal(document.getElementById('monthYearModal'));
            monthYearModal.show();
        });
    }

    // Year increase/decrease buttons
    const decreaseYear = getElement('decreaseYear');
    const increaseYear = getElement('increaseYear');
    const yearInput = getElement('modalYearInput');

    if (decreaseYear && yearInput) {
        decreaseYear.addEventListener('click', function () {
            const currentYear = parseInt(yearInput.value);
            if (currentYear > 2000) {
                yearInput.value = currentYear - 1;
            }
        });
    }

    if (increaseYear && yearInput) {
        increaseYear.addEventListener('click', function () {
            const currentYear = parseInt(yearInput.value);
            if (currentYear < 2099) {
                yearInput.value = currentYear + 1;
            }
        });
    }

    // Apply button in modal
    const applyMonthYear = getElement('applyMonthYear');
    if (applyMonthYear) {
        applyMonthYear.addEventListener('click', function () {
            const month = getElement('modalMonthSelect').value;
            const year = getElement('modalYearInput').value;

            // Validate year
            if (year < 2000 || year > 2099) {
                alert('Please enter a year between 2000 and 2099');
                return;
            }

            window.location.href = `?month=${month}&year=${year}`;
        });
    }

    // Go to Today button
    const goToToday = getElement('goToToday');
    if (goToToday) {
        goToToday.addEventListener('click', function () {
            const today = new Date();
            const month = today.getMonth() + 1;
            const year = today.getFullYear();
            window.location.href = `?month=${month}&year=${year}`;
        });
    }

    // Initialize button handlers
    initializeButtonHandlers();

    // Preserve pagination parameters when changing month/year
    const currentPage = config.currentPage;
    if (currentPage > 1) {
        const newUrl = window.location.pathname +
            `?month=${config.selectedMonth}&year=${config.selectedYear}&page=` + currentPage;
        window.history.replaceState({}, '', newUrl);
    }

    // Make calendar days clickable
    document.addEventListener('click', function (e) {
        const calendarDay = e.target.closest('.calendar-day:not(.empty)');
        if (calendarDay) {
            showAttendanceDetails(calendarDay);
        }
    });

    initializeAjaxSorting();

    // Set initial timer values from config
    if (config.todayAttendance) {
        setupTimersFromAttendance(config.todayAttendance);
    }

    // Update working and break time displays initially
    updateWorkingTimeDisplay();
    updateBreakTimeDisplay();
}

// ========== TIMER SETUP FROM RAZOR DATA ==========
function setupTimersFromAttendance(attendance) {
    try {
        if (attendance.hasClockIn && !attendance.hasClockOut) {
            // If user is already clocked in today
            const now = new Date();
            const todayStr = now.toISOString().split('T')[0];
            const clockInDateTime = new Date(todayStr + 'T' + attendance.clockInTime);

            if (!isNaN(clockInDateTime.getTime())) {
                // Calculate total elapsed time since clock in
                const totalElapsedSeconds = Math.floor((now - clockInDateTime) / 1000);

                if (attendance.isOnBreak && attendance.breakStartTime) {
                    // User is currently on break
                    const breakStartDateTime = new Date(attendance.breakStartTime);
                    if (!isNaN(breakStartDateTime.getTime())) {
                        // Calculate how long they've been on break
                        const currentBreakSeconds = Math.floor((now - breakStartDateTime) / 1000);
                        breakSeconds = Math.max(0, currentBreakSeconds);

                        // Working seconds = total time - current break time - previous break time
                        const previousBreakSeconds = attendance.totalBreakTime ?
                            (attendance.totalBreakTime.hours * 3600) +
                            (attendance.totalBreakTime.minutes * 60) +
                            attendance.totalBreakTime.seconds : 0;

                        workingSeconds = Math.max(0, totalElapsedSeconds - currentBreakSeconds - previousBreakSeconds);

                        // CRITICAL: Stop working timer and start break timer
                        stopWorkingTimer();
                        startBreakTimer(); // Start counting current break
                    } else {
                        workingSeconds = Math.max(0, totalElapsedSeconds);
                        startWorkingTimer();
                    }
                } else if (attendance.hasTakenBreak && attendance.totalBreakTime) {
                    // User has finished their break, now working again
                    const totalBreakSeconds = (attendance.totalBreakTime.hours * 3600) +
                        (attendance.totalBreakTime.minutes * 60) +
                        attendance.totalBreakTime.seconds;

                    // Working seconds = total elapsed - total break time
                    workingSeconds = Math.max(0, totalElapsedSeconds - totalBreakSeconds);
                    breakSeconds = totalBreakSeconds;

                    updateBreakTimeDisplay();
                    startWorkingTimer(); // Resume working timer
                } else {
                    // No break taken yet
                    workingSeconds = Math.max(0, totalElapsedSeconds);
                    startWorkingTimer();
                }
            }
        } else if (attendance.hasClockOut) {
            // If day is completed
            const todayStr = new Date().toISOString().split('T')[0];
            const clockInDateTime = new Date(todayStr + 'T' + attendance.clockInTime);
            const clockOutDateTime = new Date(todayStr + 'T' + attendance.clockOutTime);

            if (!isNaN(clockInDateTime.getTime()) && !isNaN(clockOutDateTime.getTime())) {
                const totalSeconds = Math.floor((clockOutDateTime - clockInDateTime) / 1000);

                // If they took breaks, subtract break time from total
                if (attendance.totalBreakTime) {
                    const breakTime = attendance.totalBreakTime;
                    const totalBreakSeconds = (breakTime.hours * 3600) + (breakTime.minutes * 60) + breakTime.seconds;
                    workingSeconds = Math.max(0, totalSeconds - totalBreakSeconds);
                    breakSeconds = totalBreakSeconds;
                } else {
                    workingSeconds = Math.max(0, totalSeconds);
                }

                updateWorkingTimeDisplay();
            }

            if (attendance.totalBreakTime) {
                const breakTime = attendance.totalBreakTime;
                breakSeconds = (breakTime.hours * 3600) + (breakTime.minutes * 60) + breakTime.seconds;
                updateBreakTimeDisplay();
            }
        }
    } catch (e) {
        console.error('Error setting up timers:', e);
        showToast('Error setting up timers. Please refresh the page.', 'error');
    }
}

// ========== OPTIMIZATION FUNCTIONS ==========
function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

function getElement(id) {
    if (!cachedElements[id]) {
        cachedElements[id] = document.getElementById(id);
    }
    return cachedElements[id];
}

// Toast notification system
function showToast(message, type = 'info') {
    // Remove existing toasts
    const existingToast = document.querySelector('.custom-toast');
    if (existingToast) {
        existingToast.remove();
    }

    // Create toast
    const toast = document.createElement('div');
    toast.className = `custom-toast custom-toast-${type}`;
    toast.innerHTML = `
        <div class="toast-content">
            <div class="toast-icon"></div>
            <div class="toast-message">${message}</div>
            <button class="toast-close">&times;</button>
        </div>
    `;

    // Add styles if not already added
    if (!document.getElementById('toast-styles')) {
        const style = document.createElement('style');
        style.id = 'toast-styles';
        style.textContent = `
            .custom-toast {
                position: fixed;
                top: 20px;
                right: 20px;
                z-index: 9999;
                min-width: 300px;
                max-width: 400px;
                background: #FFFFFF;
                border: 1px solid #C6E6FB;
                box-shadow: 0px 4px 12px rgba(88, 134, 166, 0.25);
                border-radius: 8px;
                padding: 16px;
                font-family: 'Poppins', sans-serif;
                animation: slideIn 0.3s ease-out;
            }

            .custom-toast-error {
                border-left: 4px solid #F5533D;
            }

            .custom-toast-success {
                border-left: 4px solid #52B623;
            }

            .custom-toast-warning {
                border-left: 4px solid #FF9C00;
            }

            .custom-toast-info {
                border-left: 4px solid #5A91CB;
            }

            .toast-content {
                display: flex;
                align-items: flex-start;
                gap: 12px;
            }

            .toast-icon {
                width: 24px;
                height: 24px;
                border-radius: 50%;
                flex-shrink: 0;
            }

            .custom-toast-error .toast-icon {
                background: #F5533D;
            }

            .custom-toast-success .toast-icon {
                background: #52B623;
            }

            .custom-toast-warning .toast-icon {
                background: #FF9C00;
            }

            .custom-toast-info .toast-icon {
                background: #5A91CB;
            }

            .toast-message {
                flex: 1;
                color: #122539;
                font-size: 14px;
                line-height: 1.4;
            }

            .toast-close {
                background: none;
                border: none;
                color: #5A91CB;
                font-size: 20px;
                cursor: pointer;
                padding: 0;
                line-height: 1;
                opacity: 0.7;
                transition: opacity 0.2s;
            }

            .toast-close:hover {
                opacity: 1;
            }

            @keyframes slideIn {
                from {
                    transform: translateX(100%);
                    opacity: 0;
                }
                to {
                    transform: translateX(0);
                    opacity: 1;
                }
            }

            @keyframes fadeOut {
                from {
                    opacity: 1;
                }
                to {
                    opacity: 0;
                }
            }
        `;
        document.head.appendChild(style);
    }

    document.body.appendChild(toast);

    // Auto-remove after 5 seconds
    setTimeout(() => {
        toast.style.animation = 'fadeOut 0.3s ease-out';
        setTimeout(() => toast.remove(), 300);
    }, 5000);

    // Close button functionality
    toast.querySelector('.toast-close').addEventListener('click', () => {
        toast.remove();
    });
}

// Better error handling for geolocation
function handleGeolocationError(error, button, originalHTML) {
    let message = 'Please enable location services to continue.';

    switch (error.code) {
        case error.PERMISSION_DENIED:
            message = 'Location permission was denied. Please enable location access in your browser settings.';
            break;
        case error.POSITION_UNAVAILABLE:
            message = 'Location information is currently unavailable. Please check your connection.';
            break;
        case error.TIMEOUT:
            message = 'The request to get your location timed out. Please try again.';
            break;
        case error.UNKNOWN_ERROR:
            message = 'An unknown error occurred while getting your location.';
            break;
    }

    showToast(message, 'error');

    if (button) {
        button.disabled = false;
        button.innerHTML = originalHTML;
    }
}

// Loading state manager
function setLoading(element, isLoading, loadingText = 'Processing...') {
    const originalHTML = element.innerHTML;

    if (isLoading) {
        element.dataset.originalContent = originalHTML;
        element.disabled = true;
        element.innerHTML = `<span class="spinner-border spinner-border-sm me-2"></span> ${loadingText}`;
    } else {
        element.disabled = false;
        element.innerHTML = originalHTML;
    }
}

// ========== AJAX TABLE SORTING ==========
function initializeAjaxSorting() {
    const tableHeaders = document.querySelectorAll('#attendanceTable th.sortable');

    tableHeaders.forEach(header => {
        header.addEventListener('click', async function () {
            const column = this.dataset.sort;
            const currentOrder = this.dataset.order;
            const newOrder = currentOrder === 'asc' ? 'desc' : 'asc';

            // Update UI immediately
            tableHeaders.forEach(h => {
                h.classList.remove('asc', 'desc');
                h.dataset.order = 'desc';
            });

            this.classList.add(newOrder);
            this.dataset.order = newOrder;

            // Update current sort
            currentSort.column = column;
            currentSort.order = newOrder;

            // Show loading state
            showTableLoading(true);

            try {
                // Fetch sorted data via AJAX
                await loadSortedData(column, newOrder, 1); // Reset to page 1 when sorting
            } catch (error) {
                console.error('Sorting error:', error);
                showToast('Error sorting data. Please try again.', 'error');
            } finally {
                showTableLoading(false);
            }
        });
    });

    // Initialize pagination event listeners
    initializePagination();
}

// Function to load sorted data via AJAX
async function loadSortedData(sortColumn, sortOrder, page) {
    try {
        const response = await fetch(`/Attendance/GetSortedAttendance?page=${page}&sort=${sortColumn}&order=${sortOrder}`);

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const result = await response.json();

        if (result.success) {
            // Update table with new data
            updateTableWithData(result.data);

            // Update pagination with AJAX flag
            updatePagination(result.page, result.totalPages, result.totalRecords, true);

            // Update current sort state
            currentSort = {
                column: result.sort,
                order: result.order,
                currentPage: result.page,
                totalPages: result.totalPages
            };
        } else {
            throw new Error(result.message || 'Failed to load data');
        }
    } catch (error) {
        console.error('Error loading sorted data:', error);
        throw error;
    }
}

function updateTableWithData(data) {
    const tbody = document.getElementById('attendanceTableBody');

    if (!data || data.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="5" class="text-center text-muted py-4">
                    No attendance records found
                </td>
            </tr>
        `;
        return;
    }

    // Build new rows
    let rowsHtml = '';

    data.forEach(record => {
        // Use the status from the server (already calculated correctly in the controller)
        let statusClass = "";
        let statusText = "";

        // Check if record has a status field from the server
        if (record.status) {
            const status = record.status.toLowerCase();

            switch (status) {
                case 'on time':
                case 'present':
                    statusClass = "status-on-time";
                    statusText = "On Time";
                    break;
                case 'late':
                    statusClass = "status-late";
                    statusText = "Late";
                    break;
                case 'absent':
                    statusClass = "status-absent";
                    statusText = "Absent";
                    break;
                default:
                    // If status exists but doesn't match known values
                    statusClass = "status-on-time";
                    statusText = record.status;
                    break;
            }
        } else if (record.clockInTime && record.clockInTime !== '-') {
            // Fallback: If no status from server, assume present
            statusClass = "status-on-time";
            statusText = "Present";
        } else {
            statusText = "No Clock In";
        }

        // Build row using pre-formatted values from server
        rowsHtml += `
            <tr>
                <td>${record.date}</td>
                <td>
                    ${record.clockInTime !== '-' ? `
                        <div class="d-flex align-items-center">
                            ${statusClass ? `<span class="status-indicator ${statusClass} me-2" title="${statusText}"></span>` : ''}
                            <span>${record.clockInTime}</span>
                        </div>
                    ` : '<span class="text-muted">-</span>'}
                </td>
                <td>
                    ${record.clockOutTime !== '-' ? `<span>${record.clockOutTime}</span>` : '<span class="text-muted">-</span>'}
                </td>
                <td>${record.breakTime}</td>
                <td>${record.workingTime}</td>
            </tr>
        `;
    });

    tbody.innerHTML = rowsHtml;
}

// Function to update pagination (for both initial and AJAX)
function updatePagination(currentPage, totalPages, totalRecords, isAjax = false) {
    const paginationContainer = document.querySelector('.pagination-container');
    if (!paginationContainer) return;

    // Update pagination info
    const startRecord = ((currentPage - 1) * 10) + 1;
    const endRecord = Math.min(currentPage * 10, totalRecords);

    const paginationInfo = paginationContainer.querySelector('.pagination-info');
    if (paginationInfo) {
        paginationInfo.textContent = `Showing ${startRecord}-${endRecord} of ${totalRecords} records`;
    }

    // Get current month and year for non-AJAX links
    const monthSelect = document.getElementById('monthSelect');
    const yearSelect = document.getElementById('yearSelect');
    const currentMonth = monthSelect ? monthSelect.value : window.attendanceConfig?.selectedMonth || 1;
    const currentYear = yearSelect ? yearSelect.value : window.attendanceConfig?.selectedYear || new Date().getFullYear();

    // Update pagination navigation
    const paginationNav = paginationContainer.querySelector('.pagination');
    if (paginationNav) {
        let paginationHtml = '';

        // Previous button
        paginationHtml += `
            <li class="page-item ${currentPage === 1 ? 'disabled' : ''}">
                <a class="page-link"
                   href="${isAjax ? '#' : `?month=${currentMonth}&year=${currentYear}&page=${currentPage - 1}`}"
                   onclick="changePage(${currentPage - 1}); return false;"
                   aria-label="Previous">
                    <span aria-hidden="true">&laquo;</span>
                </a>
            </li>
        `;

        // Page numbers
        let startPage = Math.max(1, currentPage - 2);
        let endPage = Math.min(totalPages, startPage + 4);

        if (endPage - startPage < 4) {
            startPage = Math.max(1, endPage - 4);
        }

        for (let i = startPage; i <= endPage; i++) {
            paginationHtml += `
                <li class="page-item ${i === currentPage ? 'active' : ''}">
                    <a class="page-link"
                       href="${isAjax ? '#' : `?month=${currentMonth}&year=${currentYear}&page=${i}`}"
                       onclick="changePage(${i}); return false;">
                        ${i}
                    </a>
                </li>
            `;
        }

        // Next button
        paginationHtml += `
            <li class="page-item ${currentPage === totalPages ? 'disabled' : ''}">
                <a class="page-link"
                   href="${isAjax ? '#' : `?month=${currentMonth}&year=${currentYear}&page=${currentPage + 1}`}"
                   onclick="changePage(${currentPage + 1}); return false;"
                   aria-label="Next">
                    <span aria-hidden="true">&raquo;</span>
                </a>
            </li>
        `;

        paginationNav.innerHTML = paginationHtml;
    }

    // Store current state
    currentSort.currentPage = currentPage;
    currentSort.totalPages = totalPages;
}

// Modified changePage function
async function changePage(page) {
    console.log('Changing to page:', page);

    // Get current month and year values
    const monthSelect = document.getElementById('monthSelect');
    const yearSelect = document.getElementById('yearSelect');
    const currentMonth = monthSelect ? monthSelect.value : window.attendanceConfig?.selectedMonth || 1;
    const currentYear = yearSelect ? yearSelect.value : window.attendanceConfig?.selectedYear || new Date().getFullYear();

    // Navigate to the page with month/year parameters
    window.location.href = `?month=${currentMonth}&year=${currentYear}&page=${page}`;
}

// Function to show/hide table loading
function showTableLoading(show) {
    const tableBody = document.getElementById('attendanceTableBody');
    const tableHeader = document.querySelector('#attendanceTable thead');

    if (show) {
        // Add loading overlay to table
        const loadingOverlay = document.createElement('div');
        loadingOverlay.id = 'tableLoadingOverlay';
        loadingOverlay.className = 'table-loading-overlay';
        loadingOverlay.innerHTML = `
            <div class="spinner-border text-primary" role="status">
                <span class="visually-hidden">Loading...</span>
            </div>
        `;

        // Position it over the table body
        if (tableBody) {
            tableBody.style.position = 'relative';
            tableBody.appendChild(loadingOverlay);
        }

        // Disable sort headers
        const sortHeaders = document.querySelectorAll('th.sortable');
        sortHeaders.forEach(header => {
            header.style.pointerEvents = 'none';
            header.style.opacity = '0.6';
        });
    } else {
        // Remove loading overlay
        const existingOverlay = document.getElementById('tableLoadingOverlay');
        if (existingOverlay) {
            existingOverlay.remove();
        }

        // Re-enable sort headers
        const sortHeaders = document.querySelectorAll('th.sortable');
        sortHeaders.forEach(header => {
            header.style.pointerEvents = '';
            header.style.opacity = '';
        });
    }
}

// Initialize pagination event listeners
function initializePagination() {
    // Event delegation for pagination links
    document.addEventListener('click', function (e) {
        if (e.target.matches('.page-link') || e.target.closest('.page-link')) {
            e.preventDefault();
        }
    });
}

// Helper function to format time from database
function formatTimeFromDatabase(timeStr) {
    if (!timeStr) return 'N/A';

    try {
        const [hours, minutes, seconds] = timeStr.split(':');
        const hourNum = parseInt(hours, 10);
        const minuteNum = parseInt(minutes, 10);

        const date = new Date();
        date.setHours(hourNum, minuteNum, 0);

        return date.toLocaleTimeString('en-US', {
            hour: '2-digit',
            minute: '2-digit',
            hour12: true
        });
    } catch (e) {
        return timeStr;
    }
}

// ========== BUTTON HANDLERS ==========
function initializeButtonHandlers() {
    const clockInBtn = getElement('clockInBtn');
    const clockOutBtn = getElement('clockOutBtn');
    const startBreakBtn = getElement('startBreakBtn');
    const endBreakBtn = getElement('endBreakBtn');

    if (clockInBtn) {
        clockInBtn.addEventListener('click', function (e) {
            e.preventDefault();
            const originalHTML = this.innerHTML;
            setLoading(this, true, 'Clocking in...');

            // Get current location for clock in
            if (navigator.geolocation) {
                navigator.geolocation.getCurrentPosition(
                    function (position) {
                        // Validate coordinates
                        if (isValidCoordinates(position.coords.latitude, position.coords.longitude)) {
                            const clockInLat = getElement('clockInLat');
                            const clockInLng = getElement('clockInLng');

                            if (clockInLat && clockInLng) {
                                clockInLat.value = position.coords.latitude;
                                clockInLng.value = position.coords.longitude;

                                // Submit form
                                const clockInForm = getElement('clockInForm');
                                if (clockInForm) {
                                    clockInForm.submit();
                                }
                            }
                        } else {
                            showToast('Invalid location coordinates detected.', 'error');
                            setLoading(clockInBtn, false);
                            clockInBtn.innerHTML = originalHTML;
                        }
                    },
                    function (error) {
                        handleGeolocationError(error, clockInBtn, originalHTML);
                    },
                    {
                        enableHighAccuracy: true,
                        timeout: 10000,
                        maximumAge: 0
                    }
                );
            } else {
                showToast('Geolocation is not supported by your browser.', 'error');
                setLoading(clockInBtn, false);
                clockInBtn.innerHTML = originalHTML;
            }
        });
    }

    if (clockOutBtn) {
        clockOutBtn.addEventListener('click', function (e) {
            e.preventDefault();
            const originalHTML = this.innerHTML;
            setLoading(this, true, 'Clocking out...');

            // Get current location for clock out
            if (navigator.geolocation) {
                navigator.geolocation.getCurrentPosition(
                    function (position) {
                        // Validate coordinates
                        if (isValidCoordinates(position.coords.latitude, position.coords.longitude)) {
                            const clockOutLat = getElement('clockOutLat');
                            const clockOutLng = getElement('clockOutLng');

                            if (clockOutLat && clockOutLng) {
                                clockOutLat.value = position.coords.latitude;
                                clockOutLng.value = position.coords.longitude;

                                // Stop all timers when clocking out
                                stopWorkingTimer();
                                stopBreakTimer();

                                // Submit form
                                const clockOutForm = getElement('clockOutForm');
                                if (clockOutForm) {
                                    clockOutForm.submit();
                                }
                            }
                        } else {
                            showToast('Invalid location coordinates detected.', 'error');
                            setLoading(clockOutBtn, false);
                            clockOutBtn.innerHTML = originalHTML;
                        }
                    },
                    function (error) {
                        handleGeolocationError(error, clockOutBtn, originalHTML);
                    },
                    {
                        enableHighAccuracy: true,
                        timeout: 10000,
                        maximumAge: 0
                    }
                );
            } else {
                showToast('Geolocation is not supported by your browser.', 'error');
                setLoading(clockOutBtn, false);
                clockOutBtn.innerHTML = originalHTML;
            }
        });
    }

    if (startBreakBtn) {
        startBreakBtn.addEventListener('click', function (e) {
            e.preventDefault();
            const originalHTML = this.innerHTML;
            setLoading(this, true, 'Starting break...');

            // Show temporary break state while form submits
            startBreakTimerUI();

            // Submit the form
            const startBreakForm = getElement('startBreakForm');
            if (startBreakForm) {
                startBreakForm.submit();
            }
        });
    }

    if (endBreakBtn) {
        endBreakBtn.addEventListener('click', function (e) {
            e.preventDefault();
            const originalHTML = this.innerHTML;
            setLoading(this, true, 'Ending break...');

            // Stop timer UI while form submits
            stopBreakTimerUI();

            // Submit the form
            const endBreakForm = getElement('endBreakForm');
            if (endBreakForm) {
                endBreakForm.submit();
            }
        });
    }
}

function isValidCoordinates(latitude, longitude) {
    return (
        typeof latitude === 'number' &&
        typeof longitude === 'number' &&
        !isNaN(latitude) &&
        !isNaN(longitude) &&
        latitude >= -90 &&
        latitude <= 90 &&
        longitude >= -180 &&
        longitude <= 180
    );
}

// ========== CLOCK FACE FUNCTIONS ==========
function initializeClockFace() {
    const svg = getElement('analog-clock');
    if (!svg) return;

    // Set proper viewBox for the SVG
    svg.setAttribute('viewBox', '0 0 200 200');

    // Clear existing content except the base elements
    const existingElements = svg.querySelectorAll('#hour-marks, #hour-numbers');
    existingElements.forEach(el => el.remove());

    // Create groups
    const hourMarks = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    hourMarks.setAttribute('id', 'hour-marks');
    svg.insertBefore(hourMarks, svg.children[2]);

    const hourNumbers = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    hourNumbers.setAttribute('id', 'hour-numbers');
    hourNumbers.setAttribute('font-family', 'Poppins');
    hourNumbers.setAttribute('font-size', '16');
    hourNumbers.setAttribute('fill', '#475569');
    hourNumbers.setAttribute('text-anchor', 'middle');
    hourNumbers.setAttribute('alignment-baseline', 'middle');
    svg.insertBefore(hourNumbers, svg.children[3]);

    // Create hour marks (lines at 12 positions)
    for (let i = 0; i < 12; i++) {
        const angle = i * 30;
        const rad = (angle - 90) * Math.PI / 180;

        // Position for the line
        const x1 = 100 + 90 * Math.cos(rad);
        const y1 = 100 + 90 * Math.sin(rad);
        const x2 = 100 + 85 * Math.cos(rad);
        const y2 = 100 + 85 * Math.sin(rad);

        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('x1', x1);
        line.setAttribute('y1', y1);
        line.setAttribute('x2', x2);
        line.setAttribute('y2', y2);
        line.setAttribute('stroke', '#FFFFFF');
        line.setAttribute('stroke-width', '2');
        hourMarks.appendChild(line);
    }

    // Create 12-3-6-9 markers (thicker lines)
    [0, 3, 6, 9].forEach(i => {
        const angle = i * 30;
        const rad = (angle - 90) * Math.PI / 180;

        const x1 = 100 + 94 * Math.cos(rad);
        const y1 = 100 + 94 * Math.sin(rad);
        const x2 = 100 + 85 * Math.cos(rad);
        const y2 = 100 + 85 * Math.sin(rad);

        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('x1', x1);
        line.setAttribute('y1', y1);
        line.setAttribute('x2', x2);
        line.setAttribute('y2', y2);
        line.setAttribute('stroke', '#FFFFFF');
        line.setAttribute('stroke-width', '4');
        hourMarks.appendChild(line);
    });
}

function updateAnalogClock() {
    const now = new Date();
    const seconds = now.getSeconds();

    // Only update if second has changed (performance optimization)
    if (seconds === lastSecond) return;
    lastSecond = seconds;

    const hours = now.getHours() % 12;
    const minutes = now.getMinutes();
    const milliseconds = now.getMilliseconds();

    // Calculate angles
    const secondAngle = (seconds + milliseconds / 1000) * 6;
    const minuteAngle = (minutes + seconds / 60) * 6;
    const hourAngle = (hours + minutes / 60) * 30;

    // Apply rotation - cache elements
    const hourHand = clockElements.hourHand || (clockElements.hourHand = getElement('hour-hand'));
    const minuteHand = clockElements.minuteHand || (clockElements.minuteHand = getElement('minute-hand'));
    const secondHand = clockElements.secondHand || (clockElements.secondHand = getElement('second-hand'));

    if (hourHand) hourHand.setAttribute('transform', `rotate(${hourAngle}, 100, 100)`);
    if (minuteHand) minuteHand.setAttribute('transform', `rotate(${minuteAngle}, 100, 100)`);
    if (secondHand) secondHand.setAttribute('transform', `rotate(${secondAngle}, 100, 100)`);

    // Update digital display - cache elements
    const digitalTime = clockElements.digitalTime || (clockElements.digitalTime = getElement('digital-time'));
    const amPm = clockElements.amPm || (clockElements.amPm = getElement('am-pm'));
    const dateElement = clockElements.dateElement || (clockElements.dateElement = getElement('liveDateShort'));

    if (digitalTime) {
        const timeString = now.toLocaleTimeString('en-US', {
            hour12: true,
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
        digitalTime.textContent = timeString.replace(/ AM| PM/, '');
    }

    if (amPm) {
        amPm.textContent = now.getHours() >= 12 ? 'PM' : 'AM';
    }

    if (dateElement) {
        const dateString = now.toLocaleDateString('en-US', {
            weekday: 'long',
            month: 'long',
            day: 'numeric'
        });
        dateElement.textContent = dateString;
    }
}

// ========== TIMER FUNCTIONS ==========
function startWorkingTimer() {
    if (workingTimer) return;

    workingTimer = setInterval(function () {
        workingSeconds++;
        updateWorkingTimeDisplay();
    }, 1000);
}

function stopWorkingTimer() {
    if (workingTimer) {
        clearInterval(workingTimer);
        workingTimer = null;
    }
}

// UI function to show break started state
function startBreakTimerUI() {
    // PAUSE working timer when break starts (keeps workingSeconds value)
    stopWorkingTimer();

    breakStartTime = new Date();

    // Start the break timer to count break duration
    startBreakTimer();

    // Update UI to show break started time
    const breakStartTimeElement = getElement('breakStartTime');
    if (breakStartTimeElement) {
        breakStartTimeElement.textContent = breakStartTime.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }
}

// UI function to show break ended state
function stopBreakTimerUI() {
    // Stop the break timer (break is over)
    stopBreakTimer();
    const clockInBtn = getElement('clockInBtn');
    const clockOutBtn = getElement('clockOutBtn');

    if (!clockInBtn && clockOutBtn) {
        startWorkingTimer(); // This continues from the paused workingSeconds
    }
}

// Actual timer functions for when break is active
function startBreakTimer() {
    if (breakTimer) return;

    breakTimer = setInterval(function () {
        breakSeconds++;
        updateBreakTimeDisplay();
    }, 1000);
}

function stopBreakTimer() {
    if (breakTimer) {
        clearInterval(breakTimer);
        breakTimer = null;
    }
}

function updateWorkingTimeDisplay() {
    const hours = Math.floor(workingSeconds / 3600);
    const minutes = Math.floor((workingSeconds % 3600) / 60);
    const seconds = workingSeconds % 60;

    const workingHoursEl = clockElements.workingHours || (clockElements.workingHours = getElement('workingHours'));
    const workingMinutesEl = clockElements.workingMinutes || (clockElements.workingMinutes = getElement('workingMinutes'));
    const workingSecondsEl = clockElements.workingSeconds || (clockElements.workingSeconds = getElement('workingSeconds'));

    if (workingHoursEl) workingHoursEl.textContent = hours;
    if (workingMinutesEl) workingMinutesEl.textContent = minutes.toString().padStart(2, '0');
    if (workingSecondsEl) workingSecondsEl.textContent = seconds.toString().padStart(2, '0');
}

function updateBreakTimeDisplay() {
    const hours = Math.floor(breakSeconds / 3600);
    const minutes = Math.floor((breakSeconds % 3600) / 60);
    const seconds = breakSeconds % 60;

    const breakHoursEl = clockElements.breakHours || (clockElements.breakHours = getElement('breakHours'));
    const breakMinutesEl = clockElements.breakMinutes || (clockElements.breakMinutes = getElement('breakMinutes'));
    const breakSecondsEl = clockElements.breakSeconds || (clockElements.breakSeconds = getElement('breakSeconds'));

    if (breakHoursEl) breakHoursEl.textContent = hours;
    if (breakMinutesEl) breakMinutesEl.textContent = minutes.toString().padStart(2, '0');
    if (breakSecondsEl) breakSecondsEl.textContent = seconds.toString().padStart(2, '0');
}

// Time display initialization from config
function initializeTimeDisplaysFromConfig(config) {
    // Update working and break time displays initially
    updateWorkingTimeDisplay();
    updateBreakTimeDisplay();

    // Start timers if needed based on config
    if (config.todayAttendance) {
        const attendance = config.todayAttendance;
        // Only start working timer if clocked in, not clocked out, AND not on break
        if (attendance.hasClockIn && !attendance.hasClockOut && !attendance.isOnBreak) {
            startWorkingTimer();
        }
    }
}

// ========== EVENT LISTENER OPTIMIZATION ==========
// Use event delegation for better performance
document.addEventListener('click', function (e) {
    // Handle pagination clicks
    if (e.target.matches('.page-link') || e.target.closest('.page-link')) {
        const pageLink = e.target.matches('.page-link') ? e.target : e.target.closest('.page-link');
        if (pageLink && !pageLink.parentElement.classList.contains('disabled')) {
            // Add loading state to pagination
            const paginationContainer = pageLink.closest('.pagination-container');
            if (paginationContainer) {
                paginationContainer.style.opacity = '0.7';
                paginationContainer.style.pointerEvents = 'none';
            }
        }
    }

    // Handle table sorting if implemented later
    if (e.target.matches('.table th') || e.target.closest('.table th')) {
        // Future table sorting implementation
    }
});

// Handle window resize with debouncing
window.addEventListener('resize', debounce(function () {
    // Update any responsive elements
    updateResponsiveElements();
}, 250));

function updateResponsiveElements() {
    // Future responsive updates
}

function showAttendanceDetails(calendarDayElement) {
    const date = calendarDayElement.dataset.date;
    const attendanceId = calendarDayElement.dataset.attendanceId;

    // Get modal element
    const modalElement = document.getElementById('attendanceModal');

    // Clean up any existing modal issues first
    cleanupModalBackdrop();

    // Set modal title
    const modalTitle = document.getElementById('attendanceDate');
    const dateObj = new Date(date);
    modalTitle.innerHTML = `<i class="bi bi-calendar-check me-2"></i>${dateObj.toLocaleDateString('en-US', {
        weekday: 'long',
        year: 'numeric',
        month: 'long',
        day: 'numeric'
    })}`;

    // Show loading state
    const detailsContainer = modalElement.querySelector('.attendance-details-container');
    detailsContainer.innerHTML = `
        <div class="loading-state">
            <div class="text-center py-4">
                <div class="spinner-border text-primary" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
                <p class="mt-2 text-muted">Loading attendance details...</p>
            </div>
        </div>
    `;

    // Initialize Bootstrap modal properly
    const modal = new bootstrap.Modal(modalElement, {
        backdrop: true,
        keyboard: true,
        focus: true
    });

    // Show modal
    modal.show();

    // Load attendance details after a short delay (to show loading state)
    setTimeout(() => {
        loadAttendanceDetails(date, attendanceId, calendarDayElement);
    }, 100);
}

// Helper function to clean up modal backdrop
function cleanupModalBackdrop() {
    // Remove all existing backdrops
    const backdrops = document.querySelectorAll('.modal-backdrop');
    backdrops.forEach(backdrop => backdrop.remove());

    // Remove modal-open class
    document.body.classList.remove('modal-open');

    // Reset body style
    document.body.style.paddingRight = '';
    document.body.style.overflow = '';
}

function forceCloseModal() {
    const modalElement = document.getElementById('attendanceModal');
    if (modalElement) {
        const modal = bootstrap.Modal.getInstance(modalElement);
        if (modal) {
            modal.hide();
        }

        // Force cleanup
        cleanupModalBackdrop();
        modalElement.style.display = 'none';

        // Show toast notification
        showToast('Modal closed', 'info');
    }
}

// You can bind this to ESC key or a hidden button for emergencies
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        forceCloseModal();
    }
});

async function loadAttendanceDetails(date, attendanceId, calendarDayElement) {
    try {
        // Show loading state
        const detailsContainer = document.querySelector('.attendance-details-container');
        detailsContainer.innerHTML = `
            <div class="loading-state">
                <div class="text-center py-4">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Loading...</span>
                    </div>
                    <p class="mt-2 text-muted">Loading attendance details...</p>
                </div>
            </div>
        `;

        // Fetch attendance details from API
        const response = await fetch(`/Attendance/GetAttendanceDetails?date=${date}`);

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const result = await response.json();

        if (!result.success) {
            throw new Error(result.message || 'Failed to fetch attendance details');
        }

        if (!result.hasAttendance) {
            // No attendance record at all for this date
            const html = generateNoAttendanceHTML(date);
            detailsContainer.innerHTML = html;
            return;
        }

        const attendance = result.attendance;
        const clockInTime = attendance.clockInTime;
        const clockOutTime = attendance.clockOutTime;
        const breakTime = attendance.totalBreakTime;
        const workingHours = attendance.workingHours;
        const status = attendance.status; // Get actual status from database

        // Generate HTML with the fetched data (pass the actual status)
        const html = generateAttendanceDetailsHTML(date, attendance.id, status, clockInTime, clockOutTime, breakTime, workingHours);
        detailsContainer.innerHTML = html;

    } catch (error) {
        showErrorState(date, error.message);
    }
}

function generateNoAttendanceHTML(date) {
    const dateObj = new Date(date);
    const formattedDate = dateObj.toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        weekday: 'long'
    });

    return `
        <div class="attendance-details show">
            <div class="detail-row">
                <span class="detail-label">Date:</span>
                <span class="detail-value">${formattedDate}</span>
            </div>
            <div class="detail-row">
                <span class="detail-label">Status:</span>
                <span class="status-badge status-absent">
                    <i class="bi bi-x-circle me-1"></i> No Record
                </span>
            </div>

            <div class="empty-state mt-4">
                <div class="icon">
                    <i class="bi bi-calendar-x"></i>
                </div>
                <h6>No Attendance Record</h6>
                <p>No attendance was recorded for this date.</p>
            </div>
        </div>
    `;
}

function generateAttendanceDetailsHTML(date, attendanceId, status, clockInTime, clockOutTime, breakTime, workingHours) {
    const dateObj = new Date(date);
    const formattedDate = dateObj.toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        weekday: 'long'
    });
    let statusClass = 'status-absent';
    let statusIcon = 'bi-x-circle';
    let statusText = 'No Record';

    if (status) {
        statusText = status;
        switch (status.toLowerCase()) {
            case 'present':
            case 'on time':
                statusClass = 'status-present';
                statusIcon = 'bi-check-circle';
                statusText = 'On Time';
                break;
            case 'late':
                statusClass = 'status-late';
                statusIcon = 'bi-clock-history';
                statusText = 'Late';
                break;
            case 'absent':
                statusClass = 'status-absent';
                statusIcon = 'bi-x-circle';
                statusText = 'Absent';
                break;
            case 'completed':
                statusClass = 'status-present';
                statusIcon = 'bi-check-circle';
                statusText = 'Completed';
                break;
            default:
                // Fallback logic if status is not recognized
                statusText = status; // Keep the original status text
                break;
        }
    } else {
        // No status in database, but we have attendance record, Check if it's a valid attendance (has clock in or clock out)
        if (clockInTime || clockOutTime) {
            if (clockInTime && clockOutTime) {
                statusClass = 'status-present';
                statusIcon = 'bi-check-circle';
                statusText = 'On Time';
            } else if (clockInTime) {
                statusClass = 'status-incomplete';
                statusIcon = 'bi-clock';
                statusText = 'In Progress';
            } else if (clockOutTime) {
                statusClass = 'status-incomplete';
                statusIcon = 'bi-clock';
                statusText = 'Clock Out Only';
            }
        } else {
            // No clock in or clock out - treat as no record
            statusClass = 'status-absent';
            statusIcon = 'bi-x-circle';
            statusText = 'No Record';
        }
    }

    // Format break time
    let breakTimeDisplay = '0 Hr 00 Mins 00 Secs';
    if (breakTime) {
        breakTimeDisplay = `${breakTime.hours} Hr ${breakTime.minutes.toString().padStart(2, '0')} Mins ${breakTime.seconds.toString().padStart(2, '0')} Secs`;
    }

    // Format working hours
    let workingHoursDisplay = '-';
    if (workingHours) {
        workingHoursDisplay = `${workingHours.hours} Hr ${workingHours.minutes.toString().padStart(2, '0')} Mins ${workingHours.seconds.toString().padStart(2, '0')} Secs`;
    }

    // Check if we should show the "No Record" empty state
    const shouldShowEmptyState = !clockInTime && !clockOutTime && (!status || status.toLowerCase() === 'absent');

    return `
        <div class="attendance-details show">
            <div class="detail-row">
                <span class="detail-label">Date:</span>
                <span class="detail-value">${formattedDate}</span>
            </div>
            <div class="detail-row">
                <span class="detail-label">Status:</span>
                <span class="status-badge ${statusClass}">
                    <i class="bi ${statusIcon} me-1"></i> ${statusText}
                </span>
            </div>

            ${clockInTime ? `
                <div class="detail-row">
                    <span class="detail-label">Clock In:</span>
                    <span class="detail-value time-display">${formatTimeFromDatabase(clockInTime)}</span>
                </div>
            ` : ''}

            ${clockOutTime ? `
                <div class="detail-row">
                    <span class="detail-label">Clock Out:</span>
                    <span class="detail-value time-display">${formatTimeFromDatabase(clockOutTime)}</span>
                </div>
            ` : ''}

            ${clockInTime || clockOutTime ? `
                <div class="detail-row">
                    <span class="detail-label">Break Time:</span>
                    <span class="detail-value">${breakTimeDisplay}</span>
                </div>
            ` : ''}

            ${workingHoursDisplay !== '-' ? `
                <div class="detail-row">
                    <span class="detail-label">Working Hours:</span>
                    <span class="detail-value">${workingHoursDisplay}</span>
                </div>
            ` : ''}

            ${shouldShowEmptyState ? `
                <div class="empty-state mt-4">
                    <div class="icon">
                        <i class="bi bi-calendar-x"></i>
                    </div>
                    <h6>No Attendance Record</h6>
                    <p>No attendance was recorded for this date.</p>
                </div>
            ` : ''}
        </div>
    `;
}

// Helper function to format time from database (HH:mm:ss format)
function formatTimeFromDatabase(timeStr) {
    if (!timeStr) return 'N/A';

    try {
        // Parse HH:mm:ss format
        const [hours, minutes, seconds] = timeStr.split(':');
        const hourNum = parseInt(hours, 10);
        const minuteNum = parseInt(minutes, 10);
        const secondNum = parseInt(seconds, 10);
        const date = new Date();
        date.setHours(hourNum, minuteNum, secondNum);
        const ampm = hourNum >= 12 ? 'PM' : 'AM';
        const hour12 = hourNum % 12 || 12;
        return `${hour12}:${minuteNum.toString().padStart(2, '0')} ${ampm} ${secondNum.toString().padStart(2, '0')}s`;
    } catch (e) {
        return timeStr;
    }
}

function formatTimeForDisplay(timeString) {
    // Convert time like "09:15 AM" or "17:30" to proper format
    if (!timeString) return 'N/A';
    if (timeString.includes('AM') || timeString.includes('PM')) {
        return timeString;
    }

    try {
        const [hours, minutes] = timeString.split(':');
        const hourNum = parseInt(hours, 10);
        const minuteNum = parseInt(minutes, 10);

        const date = new Date();
        date.setHours(hourNum, minuteNum, 0);

        return date.toLocaleTimeString('en-US', {
            hour: '2-digit',
            minute: '2-digit',
            hour12: true
        });
    } catch (e) {
        return timeString;
    }
}

function calculateWorkingHours(clockInStr, clockOutStr) {
    try {
        // Parse times with seconds support
        const parseTime = (timeStr) => {
            // Clean the time string (remove AM/PM if present)
            let cleanTime = timeStr.replace(/\s*(AM|PM)/i, '').trim();
            const parts = cleanTime.split(':');
            let hours = parseInt(parts[0], 10);
            let minutes = parseInt(parts[1] || 0, 10);
            let seconds = parseInt(parts[2] || 0, 10);

            // Handle AM/PM conversion if original had it
            if (timeStr.toLowerCase().includes('pm') && hours < 12) {
                hours += 12;
            }
            if (timeStr.toLowerCase().includes('am') && hours === 12) {
                hours = 0;
            }

            return { hours, minutes, seconds };
        };

        const start = parseTime(clockInStr);
        const end = parseTime(clockOutStr);

        // Convert to total seconds
        const startTotalSeconds = start.hours * 3600 + start.minutes * 60 + start.seconds;
        const endTotalSeconds = end.hours * 3600 + end.minutes * 60 + end.seconds;

        // Calculate difference
        let diffSeconds = endTotalSeconds - startTotalSeconds;

        // Handle negative (overnight) by adding 24 hours
        if (diffSeconds < 0) {
            diffSeconds += 24 * 3600;
        }

        // Calculate hours, minutes, seconds
        const hours = Math.floor(diffSeconds / 3600);
        const minutes = Math.floor((diffSeconds % 3600) / 60);
        const seconds = diffSeconds % 60;

        return `${hours} Hr ${minutes.toString().padStart(2, '0')} Mins ${seconds.toString().padStart(2, '0')} Secs`;

    } catch (e) {
        return '0 Hr 00 Mins 00 Secs';
    }
}

function showErrorState(date) {
    const detailsContainer = document.querySelector('.attendance-details-container');
    const dateObj = new Date(date);
    const formattedDate = dateObj.toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric'
    });

    detailsContainer.innerHTML = `
        <div class="attendance-details show">
            <div class="detail-row">
                <span class="detail-label">Date:</span>
                <span class="detail-value">${formattedDate}</span>
            </div>
            <div class="empty-state mt-4">
                <div class="icon">
                    <i class="bi bi-exclamation-triangle text-warning"></i>
                </div>
                <h6>Error Loading Data</h6>
                <p>Failed to load attendance details. Please try again.</p>
                <button class="btn btn-sm btn-outline-primary mt-3" onclick="retryLoadDetails('${date}')">
                    <i class="bi bi-arrow-clockwise me-1"></i> Retry
                </button>
            </div>
        </div>
    `;
}

function retryLoadDetails(date) {
    const calendarDay = document.querySelector(`.calendar-day[data-date="${date}"]`);
    if (calendarDay) {
        showAttendanceDetails(calendarDay);
    }
}