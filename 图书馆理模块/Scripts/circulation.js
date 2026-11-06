(function ($) {
    "use strict";
    var base = ($("body").attr("data-api-base") || "/api").replace(/\/$/, "");
    var data = { books: [], copies: [], readers: [], loans: [], reservations: [] };
    var loaded = false, busy = false, serial = 0;
    var activeTab = "copies", readerEditingId = null;

    function text(value, empty) {
        return value === null || value === undefined || String(value).trim() === ""
            ? (empty || "—") : String(value);
    }

    function code(value) {
        return (value || "").trim().toUpperCase();
    }

    function emptyMessage(xhr) {
        if (xhr && xhr.responseJSON) {
            if (xhr.responseJSON.ModelState) {
                var items = [];
                $.each(xhr.responseJSON.ModelState, function (_, messages) {
                    items = items.concat(messages);
                });
                if (items.length) return items.join(" ");
            }
            if (xhr.responseJSON.Message) return xhr.responseJSON.Message;
        }
        if (xhr && xhr.status === 403) return "当前页面不允许操作。请从本机启动网站并使用同源页面访问。";
        return "无法连接馆藏流通服务。请检查 SQL Server 连接、权限及 CIRCULATION_SCHEMA.sql 与 CIRCULATION_UPGRADE_V2.sql 是否已人工部署。";
    }

    function banner(message, error) {
        $("#circ-banner")
            .text(message)
            .toggleClass("is-error", !!error)
            .toggleClass("is-success", !error);
    }

    function date(value) {
        if (!value) return null;
        var parsed = String(value);
        // SQL datetime2 is UTC, while the serializer can omit timezone information.
        if (!/[zZ]$|[+-]\d\d:\d\d$/.test(parsed)) parsed += "Z";
        var result = new Date(parsed);
        return isNaN(result.getTime()) ? null : result;
    }

    function displayDate(value) {
        var d = date(value);
        return d ? d.toLocaleDateString("zh-CN", { year: "numeric", month: "2-digit", day: "2-digit" }) : "—";
    }

    function td(value, className) {
        return $("<td>").addClass(className || "").text(text(value));
    }

    function label(value, type) {
        return $("<span>").addClass("circ-status" + (type ? " is-" + type : "")).text(value);
    }

    function setOptions(selector, items, placeholder, name) {
        var previous = $(selector).val();
        var $select = $(selector).empty();
        $("<option>").val("").text(placeholder).appendTo($select);
        items.forEach(function (item) {
            $("<option>").val(item.id).text(item.label).appendTo($select);
        });
        if (previous && items.some(function (item) { return String(item.id) === String(previous); })) {
            $select.val(previous);
        }
        $select.prop("disabled", !loaded || items.length === 0 || busy);
        $select.attr("aria-label", name);
    }

    function renderCopies() {
        var $body = $("#copies-body").empty();
        data.copies.forEach(function (copy) {
            $("<tr>").append(
                td(copy.Barcode, "primary"),
                td(copy.BookName),
                td(copy.ShelfLocation),
                $("<td>").append(label(copy.IsBorrowed ? "借出中" : "在馆可借", copy.IsBorrowed ? "out" : ""))
            ).appendTo($body);
        });
        $("#copies-tally").text(data.copies.length + " 册");
        $("#copies-empty").prop("hidden", !loaded || data.copies.length !== 0);
    }

    function beginReaderEdit(reader) {
        if (!loaded || busy) return;
        readerEditingId = reader.ReaderId;
        $("#reader-card").val(reader.CardNumber).prop("disabled", true);
        $("#reader-name").val(reader.FullName);
        $("#reader-phone").val(reader.Phone || "");
        $("#reader-form-title").text("编辑读者档案");
        $("#reader-submit").text("保存读者资料");
        $("#reader-edit-cancel").prop("hidden", false);
        $("#reader-name").trigger("focus");
    }

    function cancelReaderEdit() {
        if (busy) return;
        readerEditingId = null;
        $("#reader-form")[0].reset();
        $("#reader-card").prop("disabled", false);
        $("#reader-form-title").text("建立读者档案");
        $("#reader-submit").text("添加读者");
        $("#reader-edit-cancel").prop("hidden", true);
    }

    function renderReaders() {
        var $body = $("#readers-body").empty();
        data.readers.forEach(function (reader) {
            var $actions = $("<td>", { class: "circ-actions-cell" });
            $("<button>", { type: "button", class: "circ-action", text: "编辑" })
                .prop("disabled", busy).on("click", function () { beginReaderEdit(reader); })
                .appendTo($actions);
            $("<button>", {
                type: "button", class: "circ-action",
                text: reader.IsActive ? "停用" : "启用"
            }).prop("disabled", busy).on("click", function () {
                var action = reader.IsActive ? "停用" : "启用";
                if (!window.confirm("确定" + action + "读者「" + reader.FullName + "」吗？")) return;
                mutate("readers/" + reader.ReaderId + "/status", "POST",
                    { IsActive: !reader.IsActive }, "读者状态已更新。");
            }).appendTo($actions);
            $("<tr>").append(
                td(reader.CardNumber, "primary"), td(reader.FullName), td(reader.Phone),
                $("<td>").append(label(reader.IsActive ? "有效" : "停用",
                    reader.IsActive ? "" : "returned")), $actions
            ).appendTo($body);
        });
        $("#readers-tally").text(data.readers.length + " 位");
        $("#readers-empty").prop("hidden", !loaded || data.readers.length !== 0);
    }

    function renderLoans() {
        var $body = $("#loans-body").empty();
        var overdueOnly = $("#only-overdue").prop("checked");
        var visible = data.loans.filter(function (loan) {
            var due = date(loan.DueAtUtc);
            return !overdueOnly || (!loan.ReturnedAtUtc && due && due.getTime() < Date.now());
        });
        visible.forEach(function (loan) {
            var active = !loan.ReturnedAtUtc;
            var due = date(loan.DueAtUtc);
            var overdue = active && due && due.getTime() < Date.now();
            var $book = $("<td>").addClass("primary").text(text(loan.BookName));
            $("<small>").addClass("sub").text(text(loan.Barcode)).appendTo($book);
            var $reader = $("<td>").text(text(loan.ReaderName));
            $("<small>").addClass("sub").text(text(loan.CardNumber)).appendTo($reader);
            var $actions = $("<td>", { class: "circ-actions-cell" });
            if (active) {
                $("<button>", { type: "button", class: "circ-action", text: "归还" })
                    .prop("disabled", busy).on("click", function () { returnLoan(loan); })
                    .appendTo($actions);
                var waiting = data.reservations.some(function (reservation) {
                    return Number(reservation.Status) === 0
                        && Number(reservation.BookNum) === Number(
                            (data.copies.find(function (c) { return c.CopyId === loan.CopyId; }) || {}).BookNum)
                        && Number(reservation.ReaderId) !== Number(loan.ReaderId);
                });
                var canRenew = !overdue && Number(loan.RenewCount || 0) < 1 && !waiting;
                $("<button>", { type: "button", class: "circ-action", text: "续借" })
                    .attr("title", canRenew ? "延长14天" : "已逾期、续借次数已用完或有其他读者预约")
                    .prop("disabled", busy || !canRenew)
                    .on("click", function () {
                        if (window.confirm("确认续借《" + text(loan.BookName) + "》14天？"))
                            mutate("loans/" + loan.LoanId + "/renew", "POST", null,
                                "续借成功，借阅到期时间延长14天。");
                    }).appendTo($actions);
            } else { $("<span>").text("—").appendTo($actions); }
            $("<tr>").append(
                $book, $reader, td(displayDate(loan.DueAtUtc)),
                $("<td>").append(label(active ? overdue ? "已逾期" : "借阅中" : "已归还",
                    active ? overdue ? "overdue" : "out" : "returned")), $actions
            ).appendTo($body);
        });
        $("#loans-tally").text(visible.length + " 条");
        $("#loans-empty").text(overdueOnly ? "当前没有逾期借阅。" : "还没有借阅记录。")
            .prop("hidden", !loaded || visible.length !== 0);
    }

    function renderReservations() {
        var $body = $("#reservations-body").empty();
        data.reservations.forEach(function (reservation) {
            var status = Number(reservation.Status);
            var $action = $("<td>", { class: "circ-actions-cell" });
            if (status === 0) {
                $("<button>", { type: "button", class: "circ-action", text: "取消预约" })
                    .prop("disabled", busy).on("click", function () {
                        if (!window.confirm("确定取消《" + text(reservation.BookName) + "》的预约？")) return;
                        mutate("reservations/" + reservation.ReservationId + "/cancel", "POST",
                            null, "预约已取消，历史记录仍会保留。");
                    }).appendTo($action);
            } else { $("<span>").text("—").appendTo($action); }
            $("<tr>").append(
                td(reservation.BookName, "primary"), td(reservation.ReaderName),
                td(displayDate(reservation.RequestedAtUtc)),
                $("<td>").append(label(status === 0 ? "排队中" : status === 1 ? "已履约" : "已取消",
                    status === 0 ? "out" : "returned")), $action
            ).appendTo($body);
        });
        $("#reservations-tally").text(data.reservations.filter(function (r) {
            return Number(r.Status) === 0;
        }).length + " 个待处理");
        $("#reservations-empty").prop("hidden", !loaded || data.reservations.length !== 0);
    }

    function renderStats() {
        if (!loaded) {
            $("#copy-count,#available-count,#reader-count,#loan-count,#overdue-count").text("—");
            return;
        }
        var available = data.copies.filter(function (copy) { return !copy.IsBorrowed; }).length;
        var readerCount = data.readers.filter(function (reader) { return reader.IsActive; }).length;
        var active = data.loans.filter(function (loan) { return !loan.ReturnedAtUtc; });
        var overdue = active.filter(function (loan) {
            var due = date(loan.DueAtUtc);
            return due && due.getTime() < Date.now();
        }).length;
        $("#copy-count").text(data.copies.length);
        $("#available-count").text(available);
        $("#reader-count").text(readerCount);
        $("#loan-count").text(active.length);
        $("#overdue-count").text(overdue + " 笔逾期记录");
    }

    function render() {
        var books = data.books.map(function (book) {
            return { id: book.BookNum, label: text(book.BookName) + " · #" + book.BookNum };
        });
        var readers = data.readers.filter(function (reader) { return reader.IsActive; })
            .map(function (reader) {
                return { id: reader.ReaderId, label: reader.FullName + " · " + reader.CardNumber };
            });
        var available = data.copies.filter(function (copy) { return !copy.IsBorrowed; })
            .map(function (copy) {
                return { id: copy.CopyId, label: text(copy.BookName) + " · " + copy.Barcode };
            });
        setOptions("#copy-book", books, "请选择已录入的书目", "选择书目");
        setOptions("#loan-reader", readers, "请选择有效读者", "选择借阅读者");
        setOptions("#loan-copy", available, "请选择在馆可借副本", "选择可借副本");
        setOptions("#reservation-book", books, "请选择预约书目", "预约书目");
        setOptions("#reservation-reader", readers, "请选择有效读者", "预约读者");
        $(".circ-form button[type=submit]").prop("disabled", !loaded || busy);
        renderStats();
        renderCopies();
        renderReaders();
        renderLoans();
        renderReservations();
    }

    function load(message) {
        var current = ++serial;
        loaded = false;
        banner("正在读取书目、副本、读者、借阅及预约记录…", false);
        render();
        var reqs = [
            $.ajax({ url: base + "/Books", method: "GET", cache: false }),
            $.ajax({ url: base + "/circulation/copies", method: "GET", cache: false }),
            $.ajax({ url: base + "/circulation/readers", method: "GET", cache: false }),
            $.ajax({ url: base + "/circulation/loans", method: "GET", cache: false }),
            $.ajax({ url: base + "/circulation/reservations", method: "GET", cache: false })
        ];
        return $.when.apply($, reqs).done(function (books, copies, readers, loans, reservations) {
            if (current !== serial) return;
            var results = [books[0], copies[0], readers[0], loans[0], reservations[0]];
            if (!results.every($.isArray)) {
                banner("接口数据结构不正确，请检查服务器版本。", true);
                return;
            }
            data.books = results[0];
            data.copies = results[1];
            data.readers = results[2];
            data.loans = results[3];
            data.reservations = results[4];
            loaded = true;
            banner(message || (window.__circulationDemo
                ? "演示模式：所有操作仅修改浏览器内存，未连接 SQL Server。"
                : "数据已更新。馆藏信息来自所连接的 SQL Server。"), false);
            render();
        }).fail(function (xhr) {
            if (current !== serial) return;
            loaded = false;
            data = { books: [], copies: [], readers: [], loans: [], reservations: [] };
            banner(emptyMessage(xhr), true);
            render();
        });
    }

    function switchTab(tab, focus) {
        activeTab = tab;
        ["copies", "readers", "loans", "reservations"].forEach(function (item) {
            var selected = item === tab;
            $("#tab-" + item).toggleClass("is-active", selected)
                .attr("aria-selected", String(selected)).attr("tabindex", selected ? 0 : -1);
            $("#panel-" + item).prop("hidden", !selected);
        });
        if (focus) $("#tab-" + tab).trigger("focus");
    }

    function save($form, route, payload, success, method) {
        if (!loaded || busy || !$form[0].reportValidity()) return;
        busy = true;
        render();
        $.ajax({
            url: base + "/circulation/" + route,
            method: method || "POST",
            contentType: "application/json; charset=utf-8",
            data: JSON.stringify(payload)
        }).done(function () {
            busy = false;
            if (route.indexOf("readers") === 0) cancelReaderEdit();
            else $form[0].reset();
            load(success);
        }).fail(function (xhr) {
            busy = false;
            render();
            banner(emptyMessage(xhr), true);
        });
    }

    function mutate(route, method, payload, message) {
        if (!loaded || busy) return;
        busy = true;
        render();
        var options = { url: base + "/circulation/" + route, method: method || "POST" };
        if (payload !== null) {
            options.contentType = "application/json; charset=utf-8";
            options.data = JSON.stringify(payload);
        }
        $.ajax(options).done(function () {
            busy = false;
            load(message);
        }).fail(function (xhr) {
            busy = false;
            render();
            banner(emptyMessage(xhr), true);
        });
    }

    function returnLoan(loan) {
        if (!loaded || busy || !loan || loan.ReturnedAtUtc) return;
        if (!window.confirm("确认归还《" + text(loan.BookName) + "》\n实体册号：" + loan.Barcode + "？")) return;
        busy = true;
        render();
        $.ajax({
            url: base + "/circulation/loans/" + encodeURIComponent(loan.LoanId) + "/return",
            method: "POST"
        }).done(function () {
            busy = false;
            load("实体副本已归还并恢复为可借状态。");
        }).fail(function (xhr) {
            busy = false;
            render();
            banner(emptyMessage(xhr), true);
        });
    }

    $(function () {
        ["copies", "readers", "loans", "reservations"].forEach(function (name) {
            $("#tab-" + name).on("click", function () { switchTab(name, false); });
        });
        $(".circ-tabs").on("keydown", function (event) {
            if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
            var tabs = ["copies", "readers", "loans", "reservations"];
            var offset = event.key === "ArrowRight" ? 1 : -1;
            var next = tabs[(tabs.indexOf(activeTab) + offset + tabs.length) % tabs.length];
            event.preventDefault();
            switchTab(next, true);
        });
        $("#refresh-circulation").on("click", function () { if (!busy) load(); });

        $("#copy-form").on("submit", function (event) {
            event.preventDefault();
            save($(this), "copies", {
                BookNum: Number($("#copy-book").val()),
                Barcode: code($("#copy-barcode").val()),
                ShelfLocation: ($("#copy-location").val() || "").trim()
            }, "实体副本已登记。可在馆藏副本列表查看。");
        });
        $("#reader-form").on("submit", function (event) {
            event.preventDefault();
            var payload = {
                FullName: ($("#reader-name").val() || "").trim(),
                Phone: ($("#reader-phone").val() || "").trim()
            };
            if (readerEditingId !== null) {
                save($(this), "readers/" + readerEditingId,
                    payload, "读者档案已更新。", "PUT");
            } else {
                payload.CardNumber = code($("#reader-card").val());
                save($(this), "readers", payload, "读者档案已建立。");
            }
        });
        $("#reader-edit-cancel").on("click", cancelReaderEdit);
        $("#only-overdue").on("change", renderLoans);
        $("#reservation-form").on("submit", function (event) {
            event.preventDefault();
            save($(this), "reservations", {
                BookNum: Number($("#reservation-book").val()),
                ReaderId: Number($("#reservation-reader").val())
            }, "预约已登记，按顺序排队。");
        });

        $("#loan-form").on("submit", function (event) {
            event.preventDefault();
            save($(this), "loans", {
                CopyId: Number($("#loan-copy").val()),
                ReaderId: Number($("#loan-reader").val())
            }, "借阅办理成功，借期为 14 天。");
        });
        switchTab("copies", false);
        load();
    });
})(jQuery);