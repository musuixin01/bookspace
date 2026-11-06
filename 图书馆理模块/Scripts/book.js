(function ($) {
    "use strict";

    var api = ($("body").attr("data-api-base") || "/api").replace(/\/$/, "");
    var books = [], categories = [], bookStatus = "loading", categoryStatus = "loading";
    var selectedCategory = "", viewMode = "gallery", featuredBook = null, detailBook = null;
    var lastFocus = null, saving = false, deleting = false, bookRequest = 0, categoryRequest = 0;
    var actionError = "", $detail = $("#detail-overlay"), $editor = $("#editor-overlay");
    var tones = ["night", "brick", "sage", "ocean", "amber", "plum", "teal", "sand"];

    function safe(value, fallback) {
        return value === null || value === undefined || String(value).trim() === "" ? (fallback || "未录入") : String(value);
    }

    function money(value) {
        var number = Number(value);
        return isFinite(number) ? "¥" + number.toFixed(2) : "—";
    }

    function hash(text) {
        var number = 0, str = String(text || "");
        for (var i = 0; i < str.length; i++) number = (number * 31 + str.charCodeAt(i)) | 0;
        return number >>> 0;
    }

    function coverFor(book, extraClass) {
        var seed = hash(safe(book.BookName, "") + "/" + safe(book.CategoryId, ""));
        var cover = $("<div>", {
            class: "book-cover theme-" + tones[seed % tones.length] + " motif-" + (seed % 4)
                + (extraClass ? " " + extraClass : ""),
            "aria-hidden": "true"
        });
        $("<span>", { class: "cover-binding" }).appendTo(cover);
        $("<span>", { class: "cover-art" }).append(
            $("<i>", { class: "shape shape-a" }),
            $("<i>", { class: "shape shape-b" }),
            $("<i>", { class: "shape shape-c" })
        ).appendTo(cover);
        $("<span>", { class: "cover-category" }).text(safe(book.CategoryName, "未分类")).appendTo(cover);
        $("<strong>", { class: "cover-title" }).text(safe(book.BookName, "未命名书籍")).appendTo(cover);
        $("<span>", { class: "cover-author" }).text(safe(book.Author, "作者未录入")).appendTo(cover);
        $("<span>", { class: "cover-footer" }).text("BOOKSPACE  /  PERSONAL LIBRARY").appendTo(cover);
        return cover;
    }

    function bookCard(book, index) {
        var label = "查看《" + safe(book.BookName, "未命名书籍") + "》的详情";
        var $card = $("<article>", { class: "book-card" }).css("--card-index", index);
        var $button = $("<button>", {
            type: "button", class: "book-card-link", "aria-label": label
        }).on("click", function () { openDetail(book); });

        $("<span>", { class: "card-cover-stage" }).append(
            coverFor(book),
            $("<span>", { class: "card-hover-tip", "aria-hidden": "true" }).text("探索这本书 ↗")
        ).appendTo($button);
        $("<span>", { class: "card-category" }).text(safe(book.CategoryName, "未分类")).appendTo($button);
        $("<strong>", { class: "card-title" }).text(safe(book.BookName, "未命名书籍")).appendTo($button);
        $("<span>", { class: "card-author" }).text(safe(book.Author, "作者未录入")).appendTo($button);
        return $card.append($button);
    }

    function cell(value, extraClass) {
        return $("<td>", { class: extraClass || "" }).text(safe(value, "—"));
    }

    function sortedMatching() {
        var keyword = ($("#search-input").val() || "").trim().toLocaleLowerCase();
        var sort = $("#sort-books").val();
        var result = books.filter(function (book) {
            var text = [book.BookName, book.Author, book.Publisher, book.CategoryName]
                .map(function (value) { return value || ""; }).join(" ").toLocaleLowerCase();
            return (!keyword || text.indexOf(keyword) !== -1)
                && (!selectedCategory || String(book.CategoryId) === selectedCategory);
        });
        result.sort(function (a, b) {
            if (sort === "name") return String(a.BookName || "").localeCompare(String(b.BookName || ""), "zh-CN");
            if (sort === "price-up") return (Number(a.BookPrice) || 0) - (Number(b.BookPrice) || 0);
            if (sort === "price-down") return (Number(b.BookPrice) || 0) - (Number(a.BookPrice) || 0);
            return (Number(b.BookNum) || 0) - (Number(a.BookNum) || 0);
        });
        return result;
    }

    function renderNotice() {
        var text = "", error = false;
        if (bookStatus === "error") {
            text = "图书数据暂时无法读取。请检查 SQL Server 连接或服务状态。";
            error = true;
        } else if (bookStatus === "loading") {
            text = "正在从书库读取书籍…";
        } else if (actionError) {
            text = actionError; error = true;
        } else if (categoryStatus === "error") {
            text = "分类服务暂时不可用。可以浏览已有书籍，但编辑功能需要恢复分类连接。";
            error = true;
        }
        $("#notice").text(text).toggleClass("is-error", error).prop("hidden", !text);
    }

    function availableCategories() {
        if (categoryStatus === "ready") return categories;
        var found = {}, output = [];
        books.forEach(function (book) {
            var id = String(book.CategoryId);
            if (!found[id] && Number(book.CategoryId) > 0) {
                found[id] = true;
                output.push({ CategoryId: book.CategoryId, CategoryName: safe(book.CategoryName, "未分类") });
            }
        });
        return output;
    }

    function renderCategories() {
        var all = availableCategories();
        var $bar = $("#category-filters").empty();
        var $side = $("#sidebar-categories").empty();
        var choices = [{ CategoryId: "", CategoryName: "全部书籍" }].concat(all);
        choices.forEach(function (item) {
            var id = String(item.CategoryId);
            var active = id === selectedCategory;
            var count = id ? books.filter(function (book) { return String(book.CategoryId) === id; }).length : books.length;
            $("<button>", {
                type: "button", class: "filter-chip" + (active ? " is-active" : ""),
                "aria-pressed": String(active)
            }).text(item.CategoryName).on("click", function () { setCategory(id); }).appendTo($bar);
            var $link = $("<button>", {
                type: "button", class: "category-link" + (active ? " is-active" : ""),
                "aria-pressed": String(active)
            }).on("click", function () { setCategory(id); });
            $("<span>").text(item.CategoryName).appendTo($link);
            $("<span>", { class: "category-count" }).text(bookStatus === "ready" ? count : "—").appendTo($link);
            $side.append($link);
        });
        var $chooser = $("#category").empty();
        $("<option>").val("").text("请选择分类").appendTo($chooser);
        if (categoryStatus === "ready") {
            categories.forEach(function (item) {
                $("<option>").val(item.CategoryId).text(item.CategoryName).appendTo($chooser);
            });
        }
    }

    function setCategory(value) {
        selectedCategory = value;
        actionError = "";
        render();
    }

    function renderFeatured(matching) {
        var hasBook = bookStatus === "ready" && matching.length > 0 && viewMode === "gallery";
        $("#featured-section").prop("hidden", !hasBook);
        featuredBook = hasBook ? matching[0] : null;
        if (!hasBook) return;
        var book = featuredBook;
        $("#featured-index").text(String(book.BookNum).padStart(3, "0"));
        $("#featured-title").text(safe(book.BookName, "未命名书籍"));
        $("#featured-author").text(safe(book.Author, "作者未录入") + "  /  " + safe(book.CategoryName, "未分类"));
        $("#featured-cover").empty().append(coverFor(book, "book-cover-featured"));
    }

    function renderTable(matching) {
        var $table = $("#book-list").empty();
        matching.forEach(function (book) {
            var $row = $("<tr>");
            var $bookCell = $("<td>").addClass("list-book");
            $("<span>", { class: "mini-book" }).append(coverFor(book, "book-cover-mini")).appendTo($bookCell);
            $("<span>", { class: "list-book-name" }).text(safe(book.BookName, "未命名书籍")).appendTo($bookCell);
            $row.append($bookCell, cell(book.Author), cell(book.CategoryName), cell(book.Publisher), cell(money(book.BookPrice), "price"));
            var $actions = $("<td>", { class: "row-actions" });
            $("<button>", { type: "button", class: "text-action", text: "查看" }).on("click", function () { openDetail(book); }).appendTo($actions);
            $("<button>", { type: "button", class: "text-action", text: "编辑" }).on("click", function () { openEditor(book); }).appendTo($actions);
            $row.append($actions).appendTo($table);
        });
    }

    function renderEmpty(matching) {
        var empty = bookStatus === "error" || (bookStatus === "ready" && matching.length === 0);
        $("#empty-state").prop("hidden", !empty);
        if (!empty) return;
        var rawEmpty = books.length === 0 && bookStatus !== "error";
        $("#empty-title").text(bookStatus === "error" ? "书库暂时无法连接" : rawEmpty ? "你的书架，正在等待第一本书" : "没有找到相符的书籍");
        $("#empty-description").text(bookStatus === "error"
            ? "连接恢复后，你的书籍会重新出现在这里。"
            : rawEmpty ? "从一本喜欢的书开始，慢慢建立属于自己的收藏。"
                : "试试别的关键词，或者回到全部书籍。");
        $("#empty-action").text(bookStatus === "error" ? "重新连接"
            : rawEmpty ? "添加第一本书" : "清空筛选");
    }

    function render() {
        var matching = bookStatus === "ready" ? sortedMatching() : [];
        var loading = bookStatus === "loading";
        $("#nav-book-count").text(bookStatus === "ready" ? books.length : "—");
        $("#visible-count").text(bookStatus === "ready" ? String(matching.length).padStart(2, "0") : "—");
        $("#filter-hint").text(bookStatus === "ready" ? "共 " + books.length + " 册 · 当前 " + matching.length + " 册" : "");
        $("#footer-tally").text(bookStatus === "ready" ? books.length + " TITLES IN COLLECTION" : "");
        $("#section-title").contents().filter(function () { return this.nodeType === 3; }).first()
            .replaceWith(viewMode === "gallery" ? "馆藏书籍 " : "书目清单 ");        $("#section-description").text(viewMode === "gallery"
            ? "每一本书，都有一处属于自己的小小风景。"
            : "用清晰的列表，整理书目的每一项信息。");
        $("#breadcrumb-current").text(viewMode === "gallery" ? "发现书籍" : "书目清单");
        $("#book-grid").prop("hidden", viewMode !== "gallery");
        $("#book-table-section").prop("hidden", viewMode !== "list" || loading || matching.length === 0);
        $("#view-gallery").toggleClass("is-active", viewMode === "gallery").attr("aria-pressed", String(viewMode === "gallery"));
        $("#view-list").toggleClass("is-active", viewMode === "list").attr("aria-pressed", String(viewMode === "list"));
        $("#nav-shelf").toggleClass("is-active", viewMode === "gallery").attr("aria-current", viewMode === "gallery" ? "page" : null);
        $("#nav-list").toggleClass("is-active", viewMode === "list").attr("aria-current", viewMode === "list" ? "page" : null);
        renderCategories();
        renderNotice();
        renderFeatured(matching);
        var $grid = $("#book-grid").empty().attr("aria-busy", String(loading));
        if (loading) {
            for (var i = 0; i < 8; i++) {
                $grid.append($("<div>", { class: "book-skeleton", "aria-hidden": "true" }).append($("<span>"), $("<i>"), $("<i>")));
            }
        } else if (viewMode === "gallery" && matching.length > 0) {
            matching.forEach(function (book, index) { $grid.append(bookCard(book, index)); });
        }
        if (viewMode === "list") renderTable(matching);
        renderEmpty(matching);
    }

    function setView(mode, jump) {
        viewMode = mode;
        actionError = "";
        render();
        if (jump) {
            var area = document.querySelector(".collection-section");
            if (area) area.scrollIntoView({ behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth", block: "start" });
        }
    }

    function loadBooks() {
        var id = ++bookRequest;
        bookStatus = "loading";
        actionError = "";
        render();
        return $.ajax({ url: api + "/Books", method: "GET", cache: false })
            .done(function (data) {
                if (id !== bookRequest) return;
                if (!$.isArray(data)) {
                    bookStatus = "error";
                    books = [];
                } else {
                    books = data; bookStatus = "ready";
                }
                render();
            }).fail(function () {
                if (id !== bookRequest) return;
                books = []; bookStatus = "error"; render();
            });
    }

    function loadCategories() {
        var id = ++categoryRequest;
        categoryStatus = "loading";
        return $.ajax({ url: api + "/Categories", method: "GET", cache: false })
            .done(function (data) {
                if (id !== categoryRequest) return;
                categories = $.isArray(data) ? data : [];
                categoryStatus = $.isArray(data) ? "ready" : "error";
                render();
            }).fail(function () {
                if (id !== categoryRequest) return;
                categories = []; categoryStatus = "error"; render();
            });
    }

    function showDialog($dialog) {
        lastFocus = document.activeElement;
        $dialog.prop("hidden", false);
        $("body").addClass("modal-open");
        $dialog.find(".icon-close").trigger("focus");
    }

    function hideDialog($dialog, restore) {
        $dialog.prop("hidden", true);
        if ($detail.prop("hidden") && $editor.prop("hidden")) $("body").removeClass("modal-open");
        if (restore !== false) {
            if (lastFocus && document.contains(lastFocus) && lastFocus.focus) lastFocus.focus();
            else $("#add-book").trigger("focus");
        }
    }

    function openDetail(book) {
        if (!book) return;
        detailBook = book;
        $("#detail-cover").empty().append(coverFor(book, "book-cover-detail"));
        $("#detail-category").text(safe(book.CategoryName, "未分类") + "  /  BOOK PROFILE");
        $("#detail-title").text(safe(book.BookName, "未命名书籍"));
        $("#detail-author").text(safe(book.Author, "作者未录入"));
        $("#detail-publisher").text(safe(book.Publisher, "未录入"));
        $("#detail-price").text(money(book.BookPrice));
        $("#detail-id").text("#" + String(book.BookNum).padStart(4, "0"));
        showDialog($detail);
    }

    function openEditor(book, fromDetail) {
        if (saving || categoryStatus !== "ready" || !categories.length) {
            actionError = "分类数据尚未准备好，无法添加或编辑。请先刷新书库。";
            renderNotice();
            return;
        }
        if (fromDetail) hideDialog($detail, false);
        $("#book-form")[0].reset();
        $("#form-error").text("").prop("hidden", true);
        $("#book-num").val(book ? book.BookNum : "");
        $("#book-name").val(book ? book.BookName : "");
        $("#author").val(book ? book.Author : "");
        $("#publisher").val(book ? book.Publisher : "");
        $("#book-price").val(book ? book.BookPrice : 0);
        $("#category").val(book ? String(book.CategoryId) : "");
        $("#form-title").text(book ? "编辑书籍资料" : "添加一本好书");
        $("#submit-book").text(book ? "保存修改" : "保存到书库");
        showDialog($editor);
        $("#book-name").trigger("focus");
    }

    function closeEditor() {
        if (saving) return;
        hideDialog($editor);
    }

    function errorText(xhr, fallback) {
        if (xhr && xhr.responseJSON && xhr.status >= 400 && xhr.status < 500) {
            var state = xhr.responseJSON.ModelState;
            if (state) {
                var list = [];
                $.each(state, function (_, value) { list = list.concat(value); });
                if (list.length) return list.join(" ");
            }
            if (xhr.responseJSON.Message) return xhr.responseJSON.Message;
        }
        return fallback;
    }

    function saveBook(event) {
        event.preventDefault();
        if (saving || !$("#book-form")[0].reportValidity()) return;
        var name = ($("#book-name").val() || "").trim();
        var price = Number($("#book-price").val());
        var categoryId = Number($("#category").val());
        if (!name || !isFinite(price) || price < 0 || price > 9999.99
            || !Number.isInteger(categoryId) || categoryId < 1) {
            $("#form-error").text("请填写有效的书名、价格和分类。").prop("hidden", false);
            return;
        }
        var id = $("#book-num").val();
        var payload = {
            BookName: name, BookPrice: price,
            Author: ($("#author").val() || "").trim(),
            Publisher: ($("#publisher").val() || "").trim(),
            CategoryId: categoryId
        };
        saving = true;
        $("#form-error").prop("hidden", true);
        $("#submit-book").prop("disabled", true).text("正在保存…");
        $.ajax({
            url: api + "/Books" + (id ? "/" + encodeURIComponent(id) : ""),
            method: id ? "PUT" : "POST",
            contentType: "application/json; charset=utf-8",
            data: JSON.stringify(payload)
        }).done(function () {
            hideDialog($editor);
            loadBooks();
        }).fail(function (xhr) {
            $("#form-error").text(errorText(xhr, "保存失败，请检查数据库连接。")).prop("hidden", false);
        }).always(function () {
            saving = false;
            $("#submit-book").prop("disabled", false).text(id ? "保存修改" : "保存到书库");
        });
    }

    function deleteBook(book) {
        if (!book || deleting) return;
        if (!window.confirm("确定从书库移除《" + safe(book.BookName) + "》吗？删除后无法撤销。")) return;
        deleting = true;
        $("#detail-delete").prop("disabled", true);
        $.ajax({ url: api + "/Books/" + encodeURIComponent(book.BookNum), method: "DELETE" })
            .done(function () {
                hideDialog($detail);
                detailBook = null;
                loadBooks();
            }).fail(function (xhr) {
                actionError = errorText(xhr, "删除失败，请检查数据库连接后重试。");
                renderNotice();
            }).always(function () {
                deleting = false;
                $("#detail-delete").prop("disabled", false);
            });
    }

    function trapFocus(event, $dialog) {
        var $focusable = $dialog.find("button:not(:disabled), input:not(:disabled), select:not(:disabled), a[href]").filter(":visible");
        if (!$focusable.length) return;
        var first = $focusable[0], last = $focusable[$focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }

    $(function () {
        $("#nav-shelf, #view-gallery").on("click", function () { setView("gallery", this.id === "nav-shelf"); });
        $("#nav-list, #view-list").on("click", function () { setView("list", this.id === "nav-list"); });
        $("#search-input").on("input", render);
        $("#sort-books").on("change", render);
        $("#add-book").on("click", function () { openEditor(null, false); });
        $("#refresh-books").on("click", function () { loadBooks(); loadCategories(); });
        $("#empty-action").on("click", function () {
            if (bookStatus === "error") { loadBooks(); loadCategories(); }
            else if (books.length === 0) openEditor(null, false);
            else { $("#search-input").val(""); setCategory(""); }
        });
        $("#featured-open").on("click", function () { openDetail(featuredBook); });
        $("#close-detail").on("click", function () { hideDialog($detail); });
        $("#close-editor, #cancel-editor").on("click", closeEditor);
        $("#detail-edit").on("click", function () { openEditor(detailBook, true); });
        $("#detail-delete").on("click", function () { deleteBook(detailBook); });
        $("#book-form").on("submit", saveBook);
        $(".overlay").on("click", function (event) {
            if (event.target !== this) return;
            if (this === $detail[0] && !deleting) hideDialog($detail);
            if (this === $editor[0] && !saving) closeEditor();
        });
        $(document).on("keydown", function (event) {
            var active = !$editor.prop("hidden") ? $editor : (!$detail.prop("hidden") ? $detail : null);
            if (!active) return;
            if (event.key === "Escape") {
                if ((active === $editor && !saving) || (active === $detail && !deleting)) {
                    event.preventDefault();
                    hideDialog(active);
                }
            } else if (event.key === "Tab") trapFocus(event, active);
        });
        render();
        loadBooks();
        loadCategories();
    });
})(jQuery);
