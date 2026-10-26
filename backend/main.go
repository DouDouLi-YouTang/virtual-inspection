package main

import (
	"errors"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/gin-contrib/cors"
	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/glebarez/sqlite"
	"gorm.io/gorm"
)

const (
	defaultDatabasePath = "data/inspection.db"
	defaultAddress      = "127.0.0.1:8080"
)

var (
	validPriorities = map[string]bool{"low": true, "medium": true, "high": true}
	validStatuses   = map[string]bool{"open": true, "in_progress": true, "resolved": true}
)

func main() {
	dbPath := defaultDatabasePath
	if len(os.Args) > 1 {
		dbPath = os.Args[1]
	}

	db, err := openDatabase(dbPath)
	if err != nil {
		log.Fatalf("open database: %v", err)
	}

	router := newRouter(db)
	log.Printf("inspection backend listening on http://%s", defaultAddress)
	log.Printf("SQLite database: %s", dbPath)
	if err := router.Run(defaultAddress); err != nil {
		log.Fatalf("run server: %v", err)
	}
}

func openDatabase(path string) (*gorm.DB, error) {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return nil, err
	}

	db, err := gorm.Open(sqlite.Open(path), &gorm.Config{})
	if err != nil {
		return nil, err
	}
	if err := db.AutoMigrate(&Issue{}); err != nil {
		return nil, err
	}
	return db, nil
}

func newRouter(db *gorm.DB) *gin.Engine {
	gin.SetMode(gin.TestMode)
	router := gin.New()
	router.Use(gin.Logger(), gin.Recovery())
	router.Use(cors.New(cors.Config{
		AllowOrigins: []string{
			"http://127.0.0.1:5173",
			"http://localhost:5173",
		},
		AllowMethods: []string{http.MethodGet, http.MethodPost, http.MethodPatch, http.MethodOptions},
		AllowHeaders: []string{"Origin", "Content-Type", "Accept"},
		MaxAge:       12 * time.Hour,
	}))

	api := router.Group("/api")
	api.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})
	api.GET("/issues", func(c *gin.Context) {
		var issues []Issue
		if err := db.Order("created_at DESC").Find(&issues).Error; err != nil {
			c.JSON(http.StatusInternalServerError, ErrorResponse{Error: "failed to load issues"})
			return
		}
		c.JSON(http.StatusOK, issues)
	})
	api.POST("/issues", func(c *gin.Context) {
		var request CreateIssueRequest
		if err := c.ShouldBindJSON(&request); err != nil {
			c.JSON(http.StatusBadRequest, ErrorResponse{Error: "invalid JSON body"})
			return
		}

		request.Title = strings.TrimSpace(request.Title)
		if request.Title == "" {
			c.JSON(http.StatusBadRequest, ErrorResponse{Error: "title is required"})
			return
		}
		if !validPriorities[request.Priority] {
			c.JSON(http.StatusBadRequest, ErrorResponse{Error: "priority must be one of low, medium, high"})
			return
		}

		now := time.Now().UTC()
		issue := Issue{
			ID:          uuid.NewString(),
			Title:       request.Title,
			Description: strings.TrimSpace(request.Description),
			Priority:    request.Priority,
			Status:      "open",
			Position:    request.Position,
			CreatedAt:   now,
			UpdatedAt:   now,
		}
		if err := db.Create(&issue).Error; err != nil {
			c.JSON(http.StatusInternalServerError, ErrorResponse{Error: "failed to save issue"})
			return
		}
		c.JSON(http.StatusCreated, issue)
	})
	api.PATCH("/issues/:id", func(c *gin.Context) {
		var request UpdateIssueRequest
		if err := c.ShouldBindJSON(&request); err != nil {
			c.JSON(http.StatusBadRequest, ErrorResponse{Error: "invalid JSON body"})
			return
		}
		if !validStatuses[request.Status] {
			c.JSON(http.StatusBadRequest, ErrorResponse{Error: "status must be one of open, in_progress, resolved"})
			return
		}

		var issue Issue
		if err := db.First(&issue, "id = ?", c.Param("id")).Error; err != nil {
			if errors.Is(err, gorm.ErrRecordNotFound) {
				c.JSON(http.StatusNotFound, ErrorResponse{Error: "issue not found"})
				return
			}
			c.JSON(http.StatusInternalServerError, ErrorResponse{Error: "failed to load issue"})
			return
		}

		issue.Status = request.Status
		issue.UpdatedAt = time.Now().UTC()
		if err := db.Save(&issue).Error; err != nil {
			c.JSON(http.StatusInternalServerError, ErrorResponse{Error: "failed to update issue"})
			return
		}
		c.JSON(http.StatusOK, issue)
	})

	return router
}

